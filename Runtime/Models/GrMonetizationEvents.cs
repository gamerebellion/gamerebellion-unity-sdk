using System;

[Serializable]
public class GrTransactionEvent
{
    public double Amount;
    public string Currency;
    public string Type;
    public string Description;
    public string TransactionHash;
    public double? UsdValue;
    public string PlatformFee;
    public string Status;
    public string FailureReason;

    // Store purchase fields. Filled in automatically when purchase auto-tracking
    // is on; a game calling TrackTransaction by hand leaves them null and gets
    // exactly the payload it has always sent.
    public string ProductId;
    public string TransactionId;

    /// <summary>True for a sandbox or Xcode-StoreKit purchase, so test revenue is not counted as real.</summary>
    public bool? IsSandbox;

    /// <summary>
    /// When the purchase happened, ISO-8601, if that is not simply "now" -- a
    /// restored purchase is weeks old and would otherwise be counted as today's
    /// revenue.
    /// </summary>
    public string OccurredAt;
}

[Serializable]
public class GrCryptoTransactionEvent
{
    public string Amount;
    public string Currency;
    public string Type;
    public string Description;
    public string TransactionHash;
    public string BlockNumber;
    public string Blockchain;
    public string NftContractAddress;
    public string NftTokenId;
    public string NftCollection;
    public string NftRarity;
    public string NftMetadataUri;
    public string GasFee;
    public string GasLimit;
    public string GasPrice;
    public string Marketplace;
    public string WalletAddress;
    public double? UsdValue;
    public double? ExchangeRate;
    public string PlatformFee;
    public string RoyaltyFee;
    public string Status;
    public double? ConfirmationCount;
    public string FailureReason;
    public string GameItemId;
    public string PlayerWalletType;
    public string IntegrationMethod;
}
