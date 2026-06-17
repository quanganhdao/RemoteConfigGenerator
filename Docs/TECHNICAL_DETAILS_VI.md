# Chi Tiết Kỹ Thuật - Remote Config Source Generator

## Tổng Quan

Remote Config Source Generator chạy trong compile time và sinh extension class cho các class có `[RemoteConfigData]`.

Mục tiêu chính:

- Không dùng reflection tại runtime.
- Tạo lookup setter/getter trực tiếp theo key.
- Cho phép project compile cả khi Firebase chưa được cài.
- Không quản lý local storage riêng. Firebase Remote Config tự persist activated values giữa các phiên.

## Luồng Compilation

```text
1. User code
   [RemoteConfigData]
   public static partial class RemoteData { ... }

2. Roslyn gọi RemoteConfigSourceGenerator

3. Syntax receiver thu thập class declarations có attribute

4. Semantic model kiểm tra class có RemoteConfigDataAttribute

5. Generator thu thập fields/properties

6. Generator sinh RemoteDataExtensions

7. Generated source được add vào compilation
```

## Attribute Được Sinh Ra

Generator sinh attribute để user code có thể dùng trực tiếp:

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RemoteConfigDataAttribute : Attribute
{
    public string PrefsPrefix { get; set; } = "rc_";
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public sealed class RemoteConfigFieldAttribute : Attribute
{
    public string Key { get; set; }
    public bool PersistToPrefs { get; set; } = true;
    public bool SyncFromRemote { get; set; } = true;

    public RemoteConfigFieldAttribute() { }
    public RemoteConfigFieldAttribute(string key) { Key = key; }
}
```

`PrefsPrefix` và `PersistToPrefs` được giữ để không làm gãy code cũ, nhưng generator hiện không sinh storage/save/load nữa.

## Define Symbol Cho Firebase

Generated source chỉ import Firebase Remote Config khi có define:

```csharp
#if VIRTUESKY_FIREBASE_REMOTECONFIG
using Firebase.RemoteConfig;
#endif
```

Method dùng Firebase `ConfigValue` cũng chỉ được sinh trong block này:

```csharp
#if VIRTUESKY_FIREBASE_REMOTECONFIG
public static bool SetFieldValue_Generated(string fieldName, ConfigValue configValue)
{
    ...
}
#endif
```

Điều này giúp project không bị lỗi compile khi chưa install Firebase.

Runtime loader trong `Example/RemoteConfig.cs` dùng thêm:

```csharp
#if VIRTUESKY_FIREBASE
using Firebase;
#endif

#if VIRTUESKY_FIREBASE_REMOTECONFIG
using Firebase.Extensions;
using Firebase.RemoteConfig;
#endif
```

## Thu Thập Members

Generator có hai mode.

### Auto-Scan Mode

Nếu không có member nào dùng `[RemoteConfigField]`, generator lấy tất cả public static fields/properties:

```csharp
[RemoteConfigData]
public static partial class RemoteData
{
    public static int GoldReward = 100;
    public static bool EnableFeature = false;
}
```

### Manual Mode

Nếu có ít nhất một member dùng `[RemoteConfigField]`, generator chỉ lấy các member có attribute:

```csharp
[RemoteConfigData]
public static partial class RemoteData
{
    [RemoteConfigField(Key = "gold_reward")]
    public static int GoldReward = 100;

    public static int LocalOnly = 1; // Không được generate
}
```

## Generated API

Với class:

```csharp
[RemoteConfigData]
public static partial class RemoteData
{
    [RemoteConfigField(Key = "inter_time_gap")]
    public static int InterTimeGap = 30;
}
```

Generator sinh:

```csharp
public static partial class RemoteDataExtensions
{
    public static readonly Dictionary<string, Action<string>> FieldSetterLookup;
    public static readonly Dictionary<string, Func<object>> FieldGetterLookup;
    public static object GetFieldValue_Generated(string fieldName);
    public static string ExportToString_Generated();

#if VIRTUESKY_FIREBASE_REMOTECONFIG
    public static bool SetFieldValue_Generated(string fieldName, ConfigValue configValue);
#endif
}
```

Generator không còn sinh:

```csharp
IRemoteConfigStorage
RemoteDataExtensions.Storage
RemoteDataExtensions.SaveToPrefs_Generated()
RemoteDataExtensions.LoadFromPrefs_Generated()
```

## FieldSetterLookup

`FieldSetterLookup` dùng cho luồng apply value dạng string:

```csharp
public static readonly Dictionary<string, Action<string>> FieldSetterLookup =
    new Dictionary<string, Action<string>>
{
    { "inter_time_gap", value => {
        if (int.TryParse(value, out var result)) RemoteData.InterTimeGap = result;
    }},
};
```

Runtime loader dùng lookup này trước:

```csharp
if (RemoteDataExtensions.FieldSetterLookup.TryGetValue(k, out Action<string> setter))
{
    var configValue = _fbRemoteConfigInstance.GetValue(k);
    setter.Invoke(configValue.StringValue);
    continue;
}
```

## SetFieldValue_Generated

`SetFieldValue_Generated` nhận Firebase `ConfigValue`, nên chỉ được sinh khi có `VIRTUESKY_FIREBASE_REMOTECONFIG`:

```csharp
#if VIRTUESKY_FIREBASE_REMOTECONFIG
public static bool SetFieldValue_Generated(string fieldName, ConfigValue configValue)
{
    switch (fieldName)
    {
        case "inter_time_gap":
            RemoteData.InterTimeGap = (int)configValue.LongValue;
            return true;

        default:
            return false;
    }
}
#endif
```

Lợi ích:

- Truy cập field trực tiếp.
- Không reflection.
- Không cần type lookup runtime.
- Không compile lỗi khi Firebase chưa có trong project.

## FieldGetterLookup Và Export

`FieldGetterLookup` hỗ trợ đọc value theo key:

```csharp
public static readonly Dictionary<string, Func<object>> FieldGetterLookup =
    new Dictionary<string, Func<object>>
{
    { "inter_time_gap", () => RemoteData.InterTimeGap },
};
```

`ExportToString_Generated()` tạo chuỗi debug:

```text
InterTimeGap: <color='#FF0000'>30</color>
```

## Array Parsing

Generator hỗ trợ:

- `int[]`
- `float[]`

Các type này được parse từ string phân tách bằng dấu phẩy:

```text
1,2,3,4
1.5,2.5,3.5
```

Generator tự sinh helper private:

```csharp
private static int[] ParseIntArray(string value) { ... }
private static float[] ParseFloatArray(string value) { ... }
```

Vì vậy generated code không phụ thuộc helper ngoài như `RemoteConfig.GetIntArray()`.

## Settings JSON Runtime Logic

`Example/RemoteConfig.cs` hỗ trợ Firebase key có hậu tố `Settings`:

```csharp
if (k.Contains("Settings"))
{
    Dictionary<string, object> jsonDict =
        JsonConvert.DeserializeObject<Dictionary<string, object>>(
            _fbRemoteConfigInstance.GetValue(k).StringValue);

    MergeNestedKeys_Optimized(jsonDict, k.Replace("Settings", ""));
    continue;
}
```

Ví dụ:

```text
Firebase key: AdSettings
JSON child: InterTimeGap
Generated lookup key: AdInterTimeGap
```

Khai báo:

```csharp
[RemoteConfigField(Key = "AdInterTimeGap")]
public static int InterTimeGap = 30;
```

## Storage Đã Bị Loại Bỏ

Các bản cũ từng sinh storage API:

```csharp
IRemoteConfigStorage
RemoteDataExtensions.Storage
SaveToPrefs_Generated()
LoadFromPrefs_Generated()
```

Các API này đã bị loại bỏ vì Firebase Remote Config đã tự lưu activated values của phiên trước. Runtime loader hiện gọi:

1. `ActivateAsync()` để apply cached activated values.
2. `FetchAsync()` để lấy data mới.
3. `ActivateAsync()` lần nữa để activate fetched values.
4. `FirebaseMergeAllKeys_Optimized()` để copy value vào static `RemoteData`.

## Hiệu Suất

### Reflection Approach

```csharp
foreach (FieldInfo field in typeof(RemoteData).GetFields())
{
    object value = field.GetValue(null);
    field.SetValue(null, parsedValue);
}
```

Overhead:

- `GetFields()` allocation.
- `FieldInfo.GetValue()` / `SetValue()` reflection calls.
- Boxing/unboxing primitive types.
- Type comparisons tại runtime.

### Generated Approach

```csharp
if (int.TryParse(value, out var result))
    RemoteData.InterTimeGap = result;
```

Ưu điểm:

- Truy cập field trực tiếp.
- Không boxing cho primitive setter path.
- Không reflection.
- Lookup theo key O(1) với dictionary hoặc switch.

## Hạn Chế Hiện Tại

1. Chỉ hỗ trợ các type cơ bản: `int`, `float`, `string`, `bool`, `long`, `int[]`, `float[]`.
2. Không tự deserialize complex object.
3. Settings JSON là logic runtime trong `Example/RemoteConfig.cs`, không phải logic riêng của generator.
4. `PersistToPrefs` và `PrefsPrefix` không còn tác dụng thực tế.

## Debug Generated Code

### Xem Generated Files

Trong Visual Studio/Rider, mở phần Analyzer generated files, hoặc bật emit generated files trong csproj:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

Sau đó rebuild project và kiểm tra thư mục generated output.

## Kết Luận

Generator hiện tập trung vào phần quan trọng nhất: sinh code apply/read/export Remote Config nhanh, type-safe, không reflection, và không gây lỗi khi Firebase chưa được install.
