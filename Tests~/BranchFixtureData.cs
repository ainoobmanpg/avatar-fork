using UnityEngine;

public sealed class BranchFixtureData : StateMachineBehaviour
{
    public string materialPath;
    public string texturePath;
    public BranchFixtureEntry[] entries;
    public Material optionalMaterial;
}

[System.Serializable]
public sealed class BranchFixtureEntry
{
    public string materialPath;
}
