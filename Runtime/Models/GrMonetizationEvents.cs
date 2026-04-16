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
