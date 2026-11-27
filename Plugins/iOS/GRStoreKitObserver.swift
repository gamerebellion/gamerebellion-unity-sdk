//
//  GRStoreKitObserver.swift
//  StoreKit 2 purchase observer for the GameRebellion SDK
//
//  Observes the App Store directly rather than hooking any engine's IAP layer,
//  so a purchase is seen whether the game uses Unity IAP, StoreKit by hand, or
//  an Unreal plugin. One implementation serves both engines.
//
//  Why StoreKit 2 and not SKPaymentQueue: the original API is deprecated as of
//  iOS 18 / macOS 15 and Apple's availability note on it reads "No longer
//  supported". StoreKit 2 also carries price, currency and a signed JWS on the
//  transaction itself, which removes the separate SKProduct price cache that
//  StoreKit 1 would have forced on us -- and with it a whole class of
//  "purchase reported with no price" bugs.
//
//  The observer never finishes a transaction. The game owns fulfilment; an
//  observer that called finish() would break it.
//

import Foundation
import StoreKit

/// The C callback the host registers. Receives one flat JSON document per
/// purchase, UTF-8, valid only for the duration of the call.
///
/// A string rather than a wide C function signature because this file's output
/// has to be understood identically by a managed Unity build, a NativeAOT Unreal
/// build and, later, the Android connector's Java equivalent. Keeping twenty
/// arguments in step across those is how field mismatches happen.
public typealias GRPurchaseCallback = @convention(c) (UnsafePointer<CChar>?) -> Void

@available(iOS 15.0, macOS 12.0, tvOS 15.0, *)
final class GRStoreKitObserver {

    static let shared = GRStoreKitObserver()

    private let lock = NSLock()
    private var callback: GRPurchaseCallback?
    private var updatesTask: Task<Void, Never>?

    /// Transactions already emitted in this process. The core keeps the durable
    /// ledger that survives relaunches; this only stops the unfinished-transaction
    /// sweep below from racing `Transaction.updates` over the same purchase.
    private var seenInProcess = Set<String>()

    private init() {}

    func setCallback(_ callback: GRPurchaseCallback?) {
        lock.lock()
        self.callback = callback
        lock.unlock()
    }

    func start() {
        lock.lock()
        let alreadyRunning = updatesTask != nil
        lock.unlock()

        if alreadyRunning { return }

        // Live path. Registered as early as the host can manage: a transaction
        // that completes while nothing is listening is delivered here on the next
        // launch instead, but only once the listener exists.
        let task = Task.detached(priority: .background) { [weak self] in
            for await verification in Transaction.updates {
                guard let self = self else { return }
                self.handle(verification, isRestored: false)
            }
        }

        lock.lock()
        updatesTask = task
        lock.unlock()

        // Backstop. `Transaction.updates` only delivers what arrives from now on,
        // so anything the store is still holding -- a purchase completed while the
        // app was closed, an Ask to Buy approved elsewhere, a reinstall's
        // entitlements -- is swept up once at start.
        Task.detached(priority: .background) { [weak self] in
            guard let self = self else { return }
            for await verification in Transaction.unfinished {
                self.handle(verification, isRestored: true)
            }
        }
    }

    func stop() {
        lock.lock()
        updatesTask?.cancel()
        updatesTask = nil
        lock.unlock()
    }

    // MARK: - Mapping

    private func handle(_ verification: VerificationResult<Transaction>, isRestored: Bool) {
        // An unverified transaction is still reported, flagged, and left for the
        // backend to judge against the store. Dropping it here would silently lose
        // revenue on the strength of a client-side check Apple explicitly says is
        // not the authority.
        let transaction: Transaction
        var verified = true

        switch verification {
        case .verified(let value):
            transaction = value
        case .unverified(let value, _):
            transaction = value
            verified = false
        }

        let dedupKey = "\(transaction.id)-\(transaction.revocationDate != nil)"

        lock.lock()
        let isNew = seenInProcess.insert(dedupKey).inserted
        lock.unlock()

        guard isNew else { return }

        emit(document(for: transaction, isRestored: isRestored, verified: verified))
    }

    private func document(for transaction: Transaction,
                          isRestored: Bool,
                          verified: Bool) -> [String: GRJSONValue] {

        // Deliberately narrow. The renewal lineage, product type, quantity and the
        // signed JWS all used to ride along here; they are all retrievable
        // server-side from the App Store Server API given the transaction id, so
        // sending them on every purchase duplicated data the backend can already
        // reach -- the JWS alone was 2.6 KB, several times the rest of the event.
        //
        // A restored purchase is not flagged separately either: status already says
        // "restored".
        var fields: [String: GRJSONValue] = [
            "revenue_source": .string("storekit2"),
            "store": .string(GRStoreKitObserver.storeName()),
            "status": .string(GRStoreKitObserver.status(for: transaction, isRestored: isRestored)),
            "type": .string("iap"),
            "product_id": .string(transaction.productID),
            "transaction_id": .string(String(transaction.id)),
            "occurred_at": .string(GRStoreKitObserver.iso8601(transaction.purchaseDate))
        ]

        // Price and currency come off the transaction on StoreKit 2. They are
        // optional in the API (older transactions predating the field, and some
        // family-shared entitlements have none), and a purchase with no currency
        // is not revenue -- the core rejects those rather than writing a
        // zero-currency row, so send what we have and let it decide.
        if let price = transaction.price {
            fields["amount"] = .number(NSDecimalNumber(decimal: price).doubleValue)
        }

        if #available(iOS 16.0, macOS 13.0, tvOS 16.0, *) {
            if let currency = transaction.currency?.identifier {
                fields["currency"] = .string(currency)
            }
        }

        if fields["currency"] == nil, let code = GRStoreKitObserver.decodedField(transaction, "currency") {
            fields["currency"] = .string(code)
        }

        // Anything that is not the production App Store is test revenue.
        //
        // The environment has three values, not two: .production, .sandbox and
        // .xcode -- the last one being a purchase against a local StoreKit
        // configuration file. Comparing against .sandbox alone reports an Xcode
        // test purchase as real money, which is how a developer's own testing
        // ends up in a revenue chart. Comparing against .production instead means
        // an environment we do not recognise is treated as test rather than as
        // revenue, which is the failure worth having.
        if #available(iOS 16.0, macOS 13.0, tvOS 16.0, *) {
            fields["is_sandbox"] = .bool(transaction.environment != .production)
        }

        if fields["is_sandbox"] == nil,
           let environment = GRStoreKitObserver.decodedField(transaction, "environment") {
            fields["is_sandbox"] = .bool(environment.caseInsensitiveCompare("Production") != .orderedSame)
        }

        // A revoked transaction is a refund or a family-sharing removal. StoreKit
        // reports it on the same channel as a purchase, so without this it would
        // be indistinguishable from one -- and would book the sale a second time.
        if let revocation = transaction.revocationDate {
            fields["status"] = .string("revoked")
            fields["failure_reason"] = .string(GRStoreKitObserver.revocationReason(transaction))
            fields["description"] = .string("revoked at \(GRStoreKitObserver.iso8601(revocation))")
        }

        if !verified {
            fields["description"] = .string("storekit signature unverified")
        }

        return fields
    }

    private func emit(_ fields: [String: GRJSONValue]) {
        lock.lock()
        let sink = callback
        lock.unlock()

        guard let sink = sink else { return }

        let json = GRJSONValue.object(fields)
        json.withCString { pointer in
            sink(pointer)
        }
    }

    // MARK: - Field mapping

    private static func status(for transaction: Transaction, isRestored: Bool) -> String {
        if transaction.revocationDate != nil { return "revoked" }
        if isRestored { return "restored" }
        return "completed"
    }

    private static func revocationReason(_ transaction: Transaction) -> String {
        guard let reason = transaction.revocationReason else { return "revoked" }

        switch reason {
        case .developerIssue: return "developer_issue"
        case .other: return "other"
        default: return "revoked"
        }
    }

    private static func storeName() -> String {
        #if os(macOS)
        return "mac_app_store"
        #else
        return "apple_app_store"
        #endif
    }

    /// Reads a field straight off the decoded transaction payload.
    ///
    /// The fallback for anything the typed API only gained in iOS 16 --
    /// `currency` and `environment` both. The signed payload has carried them all
    /// along, so an iOS 15 device is not a reason to report a purchase with no
    /// currency, or to leave it ambiguous whether it was real.
    private static func decodedField(_ transaction: Transaction, _ name: String) -> String? {
        guard let json = try? JSONSerialization.jsonObject(with: transaction.jsonRepresentation),
              let map = json as? [String: Any] else {
            return nil
        }

        return map[name] as? String
    }

    private static let formatter: ISO8601DateFormatter = {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        return formatter
    }()

    private static func iso8601(_ date: Date) -> String {
        return formatter.string(from: date)
    }
}

// MARK: - Minimal JSON writer

/// Writes the flat document the core parses. Hand-rolled rather than
/// `JSONSerialization` so the output is guaranteed flat and key order is stable,
/// which keeps it readable in logs and keeps the core's flat-object parser valid.
enum GRJSONValue {
    case string(String)
    case number(Double)
    case bool(Bool)
    case object([String: GRJSONValue])

    /// Field order in the emitted document. Anything not listed is appended
    /// alphabetically, so a new field cannot silently go missing.
    private static let keyOrder = [
        "revenue_source", "store", "status", "type", "product_id", "transaction_id",
        "amount", "currency", "is_sandbox", "occurred_at", "failure_reason", "description"
    ]

    func serialized() -> String {
        switch self {
        case .string(let value):
            return "\"\(GRJSONValue.escape(value))\""
        case .bool(let value):
            return value ? "true" : "false"
        case .number(let value):
            guard value.isFinite else { return "0" }
            if value == value.rounded() && abs(value) < 1e15 {
                return String(Int64(value))
            }
            return String(value)
        case .object(let fields):
            var ordered = GRJSONValue.keyOrder.filter { fields[$0] != nil }
            ordered.append(contentsOf: fields.keys.filter { !GRJSONValue.keyOrder.contains($0) }.sorted())

            let body = ordered.map { key in
                "\"\(GRJSONValue.escape(key))\":\(fields[key]!.serialized())"
            }.joined(separator: ",")

            return "{\(body)}"
        }
    }

    func withCString<Result>(_ body: (UnsafePointer<CChar>) -> Result) -> Result {
        return serialized().withCString(body)
    }

    private static func escape(_ value: String) -> String {
        var out = ""
        out.reserveCapacity(value.count + 16)

        for scalar in value.unicodeScalars {
            switch scalar {
            case "\"": out += "\\\""
            case "\\": out += "\\\\"
            case "\n": out += "\\n"
            case "\r": out += "\\r"
            case "\t": out += "\\t"
            default:
                if scalar.value < 0x20 {
                    out += String(format: "\\u%04x", scalar.value)
                } else {
                    out.unicodeScalars.append(scalar)
                }
            }
        }

        return out
    }
}

// MARK: - C entry points

/// Registers the purchase sink. Pass nil to detach.
@_cdecl("gr_ios_iap_set_callback")
public func gr_ios_iap_set_callback(_ callback: GRPurchaseCallback?) {
    if #available(iOS 15.0, macOS 12.0, tvOS 15.0, *) {
        GRStoreKitObserver.shared.setCallback(callback)
    }
}

/// Starts observing. Idempotent -- calling it twice does not double-report.
/// Returns 1 when observing, 0 when the OS is too old for StoreKit 2.
@_cdecl("gr_ios_iap_start_observer")
public func gr_ios_iap_start_observer() -> Int32 {
    if #available(iOS 15.0, macOS 12.0, tvOS 15.0, *) {
        GRStoreKitObserver.shared.start()
        return 1
    }

    return 0
}

/// Stops observing. The game's own StoreKit usage is untouched.
@_cdecl("gr_ios_iap_stop_observer")
public func gr_ios_iap_stop_observer() {
    if #available(iOS 15.0, macOS 12.0, tvOS 15.0, *) {
        GRStoreKitObserver.shared.stop()
    }
}

/// 1 when this build can observe purchases at all, so the host can say why
/// nothing is being reported instead of looking broken.
@_cdecl("gr_ios_iap_is_supported")
public func gr_ios_iap_is_supported() -> Int32 {
    if #available(iOS 15.0, macOS 12.0, tvOS 15.0, *) {
        return 1
    }

    return 0
}
