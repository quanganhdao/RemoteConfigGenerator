# Hướng Dẫn Sử Dụng Nhanh - Remote Config Generator

## Bước 1: Cài Đặt Source Generator

### Dùng DLL trong Unity

1. Copy `SourceGenerator.dll` vào `Assets/Plugins/`.
2. Chọn DLL trong Unity Inspector.
3. Bỏ chọn **Any Platform**.
4. Chỉ chọn **Editor**.
5. Thêm asset label `RoslynAnalyzer`.
6. Apply.

### Hoặc tham chiếu project generator

```xml
<ItemGroup>
  <ProjectReference Include="..\SourceGenerator\RemoteConfigGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Bước 2: Define Symbol Cho Firebase

Nếu project chưa cài Firebase, không cần thêm define symbol. Code Firebase sẽ được loại khỏi compile.

Khi đã cài Firebase, thêm các symbol tương ứng trong Unity Player Settings:

| Symbol | Khi nào dùng | Tác dụng |
| --- | --- | --- |
| `VIRTUESKY_FIREBASE` | Đã cài Firebase App | Bật `FirebaseApp.CheckAndFixDependenciesAsync()`. |
| `VIRTUESKY_FIREBASE_REMOTECONFIG` | Đã cài Firebase Remote Config | Bật `Firebase.RemoteConfig`, `Firebase.Extensions`, và generated method dùng `ConfigValue`. |

## Bước 3: Tạo Remote Config Class

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

Lưu ý:

- Class phải là `static partial`.
- Class phải có `[RemoteConfigData]`.
- Dùng `[RemoteConfigField(Key = "...")]` khi key trên Firebase khác tên field.
- `PrefsPrefix` và `PersistToPrefs` chỉ còn để tương thích code cũ, không còn sinh logic storage.

## Bước 4: Sử Dụng Trong Game

### Đọc giá trị

```csharp
int interGap = RemoteData.InterTimeGap;
int startLevel = RemoteData.StartLevelShowInter;
```

### Debug giá trị hiện tại

```csharp
string debugInfo = RemoteDataExtensions.ExportToString_Generated();
Debug.Log(debugInfo);
```

Output ví dụ:

```text
InterTimeGap: 30
StartLevelShowInter: 15
```

## Bước 5: Sync Từ Firebase Remote Config

Sử dụng runtime loader trong `Example/RemoteConfig.cs`. Loader này sẽ:

1. Check Firebase dependency nếu có `VIRTUESKY_FIREBASE`.
2. Activate cached values của phiên trước.
3. Fetch remote values mới.
4. Activate fetched values.
5. Apply Firebase keys vào `RemoteData` bằng generated lookup.

Không cần gọi `SaveToPrefs_Generated()` hoặc `LoadFromPrefs_Generated()` nữa. Firebase Remote Config tự lưu activated values giữa các phiên.

## Generated API

Với class `RemoteData`, generator tạo `RemoteDataExtensions` gồm:

```csharp
public static readonly Dictionary<string, Action<string>> FieldSetterLookup;
public static readonly Dictionary<string, Func<object>> FieldGetterLookup;
public static object GetFieldValue_Generated(string fieldName);
public static string ExportToString_Generated();
```

Khi có `VIRTUESKY_FIREBASE_REMOTECONFIG`, generator tạo thêm:

```csharp
public static bool SetFieldValue_Generated(string fieldName, ConfigValue configValue);
```

Generator không còn tạo:

```csharp
RemoteDataExtensions.Storage
RemoteDataExtensions.SaveToPrefs_Generated()
RemoteDataExtensions.LoadFromPrefs_Generated()
IRemoteConfigStorage
```

## Auto-Scan Và Manual Mode

### Auto-Scan

Nếu không có field nào dùng `[RemoteConfigField]`, generator tự quét tất cả public static fields/properties:

```csharp
[RemoteConfigData]
public static partial class RemoteData
{
    public static int GoldReward = 100;
    public static bool EnableFeature = false;
}
```

### Manual Mode

Nếu có ít nhất một field dùng `[RemoteConfigField]`, generator chỉ lấy các field có attribute:

```csharp
[RemoteConfigData]
public static partial class RemoteData
{
    [RemoteConfigField(Key = "gold_reward")]
    public static int GoldReward = 100;

    public static int LocalValue = 1; // Không được generate
}
```

## Settings JSON Keys

`Example/RemoteConfig.cs` có logic xử lý Firebase key có hậu tố `Settings`.

Firebase key:

```text
AdSettings
```

Firebase value:

```json
{
  "InterTimeGap": 30,
  "StartLevelShowInter": 15
}
```

Runtime sẽ bỏ chữ `Settings`, lấy prefix là `Ad`, rồi ghép với key con trong JSON:

```text
AdInterTimeGap
AdStartLevelShowInter
```

Khai báo trong `RemoteData`:

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

## Supported Types

- `int`
- `float`
- `string`
- `bool`
- `long`
- `int[]`
- `float[]`

`int[]` và `float[]` được parse từ chuỗi phân tách bằng dấu phẩy.

## Troubleshooting

### Lỗi namespace Firebase khi chưa cài Firebase

Kiểm tra define symbols:

- Chưa cài Firebase: bỏ `VIRTUESKY_FIREBASE` và `VIRTUESKY_FIREBASE_REMOTECONFIG`.
- Đã cài Firebase App: thêm `VIRTUESKY_FIREBASE`.
- Đã cài Firebase Remote Config: thêm `VIRTUESKY_FIREBASE_REMOTECONFIG`.

### Không thấy generated code

Kiểm tra:

1. Class có `static partial`.
2. Class có `[RemoteConfigData]`.
3. Rebuild project.
4. Refresh Unity/IDE.

### Firebase key không apply vào RemoteData

Kiểm tra:

1. Key Firebase trùng với `RemoteConfigField.Key` hoặc tên field.
2. Nếu dùng `Settings`, key generated là prefix + key con JSON.
3. Type của field nằm trong danh sách supported.
4. Field không bị đặt `SyncFromRemote = false`.

## Tài Liệu Tham Khảo

- [TECHNICAL_DETAILS_VI.md](TECHNICAL_DETAILS_VI.md)
- [README.md](../README.md)
