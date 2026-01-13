namespace HSMbridge;

public class HSMbridgeOptions
{
    public const string SectionName = "HSMbridge";

    public int Port { get; set; } = 5000;
    public string? SerialPort { get; set; }
    public string Network { get; set; } = "mainnet";
    public PinServerOptions PinServer { get; set; } = new();
    public bool EnableSwagger { get; set; } = true;
    public int HsmActivationTimeoutSeconds { get; set; } = 120;
}

public class PinServerOptions
{
    public string Mode { get; set; } = "Remote";
    public string? Url { get; set; }
}
