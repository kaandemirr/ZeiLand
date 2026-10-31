using System.Text.Json.Serialization;

namespace ZeiLand.Settings;

/// <summary>When the always-on display comes up: after a while without input, or for as long as it's turned on.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AodTrigger>))]
public enum AodTrigger
{
    Idle,
    Always,
}
