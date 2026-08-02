namespace PartyGame.Api.Security;

public sealed class TransportSecurityOptions
{
    public const string SectionName = "Security:Transport";

    /// <summary>Explicit acknowledgement that plain HTTP is only for a trusted LAN.</summary>
    public bool AllowInsecureLanHttp { get; set; }
    public bool EnableHsts { get; set; }
}
