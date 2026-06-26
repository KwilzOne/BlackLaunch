using System.Collections.Generic;

namespace BlackLaunch.Models;

public class Config {
    public string Nickname { get; set; } = "";
    public string Version { get; set; } = "";
    public string Loader { get; set; } = "Vanilla";
    public List<string> CachedVersions { get; set; } = [];
}
