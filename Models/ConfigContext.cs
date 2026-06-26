using System.Text.Json.Serialization;

namespace BlackLaunch.Models;

[JsonSerializable(typeof(Config))]
internal partial class ConfigContext : JsonSerializerContext {}
