using System.Text.Json.Serialization;

namespace BlackLaunch.Models;

[JsonSerializable(typeof(Instance))]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(Config))]
internal partial class ConfigContext : JsonSerializerContext {}
