using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTCPayServer.Plugins.BTCX.Rpc;

public sealed record BtcxBlockchainInfo
{
    [JsonPropertyName("chain")] public string Chain { get; init; } = "";
    [JsonPropertyName("blocks")] public int Blocks { get; init; }
    [JsonPropertyName("headers")] public int Headers { get; init; }
    [JsonPropertyName("bestblockhash")] public string BestBlockHash { get; init; } = "";
    [JsonPropertyName("base_target")] public ulong? BaseTarget { get; init; }
    [JsonPropertyName("generation_signature")] public string? GenerationSignature { get; init; }
    [JsonPropertyName("initialblockdownload")] public bool InitialBlockDownload { get; init; }
    [JsonPropertyName("pruned")] public bool Pruned { get; init; }
}

public sealed record BtcxNetworkInfo
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("subversion")] public string Subversion { get; init; } = "";
    [JsonPropertyName("protocolversion")] public int ProtocolVersion { get; init; }
    [JsonPropertyName("localservices")] public string? LocalServices { get; init; }
    [JsonPropertyName("localrelay")] public bool? LocalRelay { get; init; }
    [JsonPropertyName("connections")] public int Connections { get; init; }
    [JsonPropertyName("networkactive")] public bool? NetworkActive { get; init; }
    [JsonPropertyName("relayfee")] public decimal? RelayFee { get; init; }
    [JsonPropertyName("incrementalfee")] public decimal? IncrementalFee { get; init; }
    [JsonPropertyName("warnings")] public JsonElement Warnings { get; init; }
}

/// <summary>PoCX block response: core fields plus PoCX-specific fields are preserved in Extensions.</summary>
public sealed record BtcxBlockInfo
{
    [JsonPropertyName("hash")] public string Hash { get; init; } = "";
    [JsonPropertyName("confirmations")] public int Confirmations { get; init; }
    [JsonPropertyName("height")] public int Height { get; init; }
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("versionHex")] public string? VersionHex { get; init; }
    [JsonPropertyName("merkleroot")] public string? MerkleRoot { get; init; }
    [JsonPropertyName("time")] public long Time { get; init; }
    [JsonPropertyName("mediantime")] public long? MedianTime { get; init; }
    [JsonPropertyName("nTx")] public int TransactionCount { get; init; }
    [JsonPropertyName("tx")] public JsonElement Transactions { get; init; }
    [JsonPropertyName("previousblockhash")] public string? PreviousBlockHash { get; init; }
    [JsonPropertyName("nextblockhash")] public string? NextBlockHash { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record BtcxDecodedTransaction
{
    [JsonPropertyName("txid")] public string TxId { get; init; } = "";
    [JsonPropertyName("hash")] public string? Hash { get; init; }
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("size")] public int Size { get; init; }
    [JsonPropertyName("vsize")] public int VirtualSize { get; init; }
    [JsonPropertyName("weight")] public int Weight { get; init; }
    [JsonPropertyName("locktime")] public uint LockTime { get; init; }
    [JsonPropertyName("hex")] public string? Hex { get; init; }
    [JsonPropertyName("confirmations")] public int? Confirmations { get; init; }
    [JsonPropertyName("blockhash")] public string? BlockHash { get; init; }
    [JsonPropertyName("in_active_chain")] public bool? InActiveChain { get; init; }
    [JsonPropertyName("time")] public long? Time { get; init; }
    [JsonPropertyName("blocktime")] public long? BlockTime { get; init; }
    [JsonPropertyName("vin")] public JsonElement Inputs { get; init; }
    [JsonPropertyName("vout")] public JsonElement Outputs { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>gettransaction is wallet-scoped and has wallet accounting fields in addition to decoded transaction data.</summary>
public sealed record BtcxWalletTransaction
{
    [JsonPropertyName("txid")] public string? TxId { get; init; }
    [JsonPropertyName("amount")] public decimal? Amount { get; init; }
    [JsonPropertyName("fee")] public decimal? Fee { get; init; }
    [JsonPropertyName("confirmations")] public int? Confirmations { get; init; }
    [JsonPropertyName("blockhash")] public string? BlockHash { get; init; }
    [JsonPropertyName("hex")] public string? Hex { get; init; }
    [JsonPropertyName("decoded")] public BtcxDecodedTransaction? Decoded { get; init; }
    [JsonPropertyName("details")] public JsonElement Details { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record BtcxTransactionConfirmationInfo(
    string TxId,
    int Confirmations,
    string? BlockHash,
    bool? InActiveChain,
    bool IsMempoolCandidate);

public sealed class BtcxRpcException(string message, string method, int? code = null, int? httpStatus = null)
    : Exception(message)
{
    public string Method { get; } = method;
    public int? RpcCode { get; } = code;
    public int? HttpStatus { get; } = httpStatus;
}

public sealed class BtcxRpcAuthenticationException()
    : Exception("BTCX node RPC authentication failed. Check the configured cookie or secret credentials.");

public sealed class BtcxRpcProtocolException(string message) : Exception(message);

public sealed class BtcxRpcUnavailableException(string message, string method) : Exception(message)
{
    public string Method { get; } = method;
}
