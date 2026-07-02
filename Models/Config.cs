using System;
using System.Collections.Generic;

namespace BlackLaunch.Models;

public class Instance {
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Loader { get; set; } = "Vanilla";
    public string LoaderVersion { get; set; } = "";
    public DateTime? LastLaunch { get; set; } = null;
    public double PlaytimeHours { get; set; } = 0;
}

public class Profile {
    public string Id { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string SkinPath { get; set; } = "";
}

public class Config {
    public List<string> CachedVersions { get; set; } = [];
    
    public List<Instance> Instances { get; set; } = [];
    public string SelectedInstanceId { get; set; } = "";
    
    public List<Profile> Profiles { get; set; } = [];
    public string SelectedProfileId { get; set; } = "";
}
