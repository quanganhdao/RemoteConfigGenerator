# Remote Config Source Generator

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET Standard 2.0](https://img.shields.io/badge/.NET%20Standard-2.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/standard/net-standard)
[![Roslyn](https://img.shields.io/badge/Roslyn-4.3.0-purple.svg)](https://github.com/dotnet/roslyn)

A C# Source Generator for Unity that creates optimized, reflection-free Remote Config access code. Firebase integration is optional and guarded by define symbols so projects can compile before Firebase packages are installed.

## Features

- **Zero reflection overhead**: generated dictionary/switch based setters and getters.
- **Type-safe fields**: compile-time field access for config values.
- **Auto-scan mode**: if no field has `[RemoteConfigField]`, all public static fields/properties are included.
- **Manual mode**: if at least one field has `[RemoteConfigField]`, only attributed fields/properties are included.
- **Firebase-safe generation**: Firebase-specific generated code is wrapped in `VIRTUESKY_FIREBASE_REMOTECONFIG`.
- **No storage layer**: Firebase Remote Config persists activated values between sessions, so the generator no longer creates `Storage`, `SaveToPrefs_Generated()`, or `LoadFromPrefs_Generated()`.

## Installation

### Unity Setup

```
Dependencies: Install NuGet For Unity, then install Microsoft.CodeAnalysis.CSharp
```

Use the `SourceGenerator.dll` from [here](https://github.com/unity-package/RemoteConfigGenerator/blob/main/SourceGenerator/bin/Release/netstandard2.0/RemoteConfigGenerator.dll), or build it yourself.

1. Copy `SourceGenerator.dll` into `Assets/Plugins/`.
2. Select the DLL in Unity Inspector.
3. Disable **Any Platform**.
4. Enable **Editor** only.
5. Add the asset label `RoslynAnalyzer`.
6. Apply changes.

## Define Symbols

The runtime example and generated code use these symbols:

| Symbol | Required when | Purpose |
| --- | --- | --- |
| `VIRTUESKY_FIREBASE` | Firebase App is installed | Enables `FirebaseApp.CheckAndFixDependenciesAsync()`. |
| `VIRTUESKY_FIREBASE_REMOTECONFIG` | Firebase Remote Config is installed | Enables `Firebase.RemoteConfig`, `Firebase.Extensions`, and generated `SetFieldValue_Generated(string, ConfigValue)`. |

When Firebase is not installed, do not define these symbols. The project should still compile because Firebase-specific code is excluded.

## Quick Start

### 1. Define Remote Data

```csharp
using RemoteConfigGenerator;

[RemoteConfigData]
public static partial class RemoteData
{
    [RemoteConfigField(Key = "inter_time_gap")]
    public static int InterTimeGap = 30;

    [RemoteConfigField(Key = "start_level_show_inter")]
    public static int StartLevelShowInter = 15;
}
```

Important:

- The class must be `static partial`.
- Add `[RemoteConfigData]`.
- Use `[RemoteConfigField(Key = "...")]` when the Firebase key differs from the C# member name.
- `PrefsPrefix` and `PersistToPrefs` are kept only for backwards compatibility. They no longer affect generated storage code.

### 2. Read Values

```csharp
int gap = RemoteData.InterTimeGap;
int startLevel = RemoteData.StartLevelShowInter;
```

### 3. Export For Debug

```csharp
string debugInfo = RemoteDataExtensions.ExportToString_Generated();
Debug.Log(debugInfo);
```

## Firebase Remote Config

Use the provided `Example/RemoteConfig.cs` as the runtime loader. It:

- Checks Firebase dependencies when `VIRTUESKY_FIREBASE` is defined.
- Activates cached Remote Config values from previous sessions.
- Fetches remote values.
- Activates fetched values.
- Applies keys through generated lookup methods.

The generator creates two sync paths:

```csharp
RemoteDataExtensions.FieldSetterLookup.TryGetValue(key, out Action<string> setter);
RemoteDataExtensions.SetFieldValue_Generated(key, configValue);
```

`SetFieldValue_Generated` is generated only when `VIRTUESKY_FIREBASE_REMOTECONFIG` is defined because it uses Firebase's `ConfigValue` type.

## Settings JSON Keys

`Example/RemoteConfig.cs` supports Firebase keys ending with `Settings`. The runtime removes `Settings` from the key and prefixes each JSON child key with the remaining name.

Firebase key:

```text
AdSettings
```

Firebase JSON value:

```json
{
  "InterTimeGap": 30,
  "StartLevelShowInter": 15
}
```

Generated lookup keys:

```text
AdInterTimeGap
AdStartLevelShowInter
```

Remote data declaration:

```csharp
[RemoteConfigData]
public static partial class RemoteData
{
    [RemoteConfigField(Key = "AdInterTimeGap")]
    public static int InterTimeGap = 30;

    [RemoteConfigField(Key = "AdStartLevelShowInter")]
    public static int StartLevelShowInter = 15;
}
```

## Advanced Usage

### Auto-Scan Mode

If no member uses `[RemoteConfigField]`, the generator includes all public static members:

```csharp
[RemoteConfigData]
public static partial class RemoteData
{
    public static int GoldReward = 100;
    public static bool EnableFeature = false;
}
```

### Manual Mode

If at least one member uses `[RemoteConfigField]`, only attributed members are included:

```csharp
[RemoteConfigData]
public static partial class RemoteData
{
    [RemoteConfigField(Key = "gold_reward")]
    public static int GoldReward = 100;

    public static int LocalOnlyValue = 1; // Not generated
}
```

### Skip Remote Sync

```csharp
[RemoteConfigField(SyncFromRemote = false)]
public static int LocalOnlyValue = 0;
```

### Supported Types

- `int`
- `float`
- `string`
- `bool`
- `long`
- `int[]`
- `float[]`

Arrays are parsed from comma-separated strings by generated helper methods.

## Building The DLL

```bash
cd SourceGenerator
dotnet build -c Release
```

Output:

```text
SourceGenerator/bin/Release/netstandard2.0/RemoteConfigGenerator.dll
```

## Generated API

For a class named `RemoteData`, the generator creates `RemoteDataExtensions` with:

```csharp
public static readonly Dictionary<string, Action<string>> FieldSetterLookup;
public static readonly Dictionary<string, Func<object>> FieldGetterLookup;
public static object GetFieldValue_Generated(string fieldName);
public static string ExportToString_Generated();
```

When `VIRTUESKY_FIREBASE_REMOTECONFIG` is defined, it also creates:

```csharp
public static bool SetFieldValue_Generated(string fieldName, ConfigValue configValue);
```

The generator no longer creates:

```csharp
RemoteDataExtensions.Storage
RemoteDataExtensions.SaveToPrefs_Generated()
RemoteDataExtensions.LoadFromPrefs_Generated()
IRemoteConfigStorage
```

## Troubleshooting

### Firebase namespace compile errors

Check your scripting define symbols:

- Firebase App installed: add `VIRTUESKY_FIREBASE`.
- Firebase Remote Config installed: add `VIRTUESKY_FIREBASE_REMOTECONFIG`.
- Firebase not installed: remove both symbols.

### Generated code not appearing

Check:

1. Class is `static partial`.
2. Class has `[RemoteConfigData]`.
3. Project was rebuilt.
4. IDE/Unity was refreshed.

### Key from Firebase is not applied

Check:

1. The Firebase key matches `[RemoteConfigField(Key = "...")]` or the member name.
2. If using `AdSettings` style JSON, the generated key is `Ad` + JSON child key.
3. The field type is supported.
4. The field is not marked `SyncFromRemote = false`.

## Documentation

- [Quick Start VI](Docs/QUICK_START_VI.md)
- [Technical Details VI](Docs/TECHNICAL_DETAILS_VI.md)

## Support

- Create an issue on GitHub.
- Check the `Example` folder for current runtime integration.
