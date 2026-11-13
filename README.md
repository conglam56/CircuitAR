# CircuitAR

Ứng dụng Unity hỗ trợ lắp và mô phỏng mạch điện cơ bản trên bàn/sàn thật bằng AR. Chế độ kính điện thoại sử dụng cùng phiên AR, bổ sung giao diện hai ô mắt và thao tác bằng hai tay.

## Chức năng

- Kho linh kiện: pin, đèn, công tắc, ampe kế và vôn kế.
- Đặt linh kiện tức thì, chọn chính xác thân vật, kéo, xoay 90° và xóa từng linh kiện.
- Chọn hai cực để nối dây; xóa dây và mô phỏng trạng thái mạch, đèn, công tắc, số đo.
- Hoàn tác/làm lại thao tác đặt, di chuyển, xoay, xóa và nối dây.
- Trong chế độ kính: hai tay thao tác độc lập, menu trong không gian, thanh kéo menu và chỉnh độ lệch hai ảnh. Thông số chỉnh ảnh được lưu trên máy.
- Giữ linh kiện và lịch sử khi neo AR bị loại bỏ; kiểm tra lại mặt đỡ khi phục hồi tracking.

## Công nghệ và môi trường

| Thành phần | Phiên bản/cấu hình |
| --- | --- |
| Unity Editor | **6000.3.24f1** — xem `ProjectSettings/ProjectVersion.txt` |
| Render pipeline | Universal Render Pipeline 17.3.0 |
| AR | AR Foundation / ARCore 6.3.5 |
| Nhận diện tay | MediaPipe Unity Plugin 0.16.3, mô hình `Assets/StreamingAssets/hand_landmarker.bytes` |
| Nền tảng ứng dụng | Android, ARM64; thiết bị cần hỗ trợ ARCore và cấp quyền camera |
| Application ID | `com.UnityAR.CircuitAR` |

Khi cài Unity bằng Unity Hub, chọn **Android Build Support**, **Android SDK & NDK Tools** và **OpenJDK**. Dự án đã chứa package MediaPipe và mô hình tay; Unity Package Manager sẽ khôi phục các package còn lại.

## Mở dự án và build Android

1. Clone repository rồi mở thư mục gốc bằng Unity Hub với phiên bản Editor nêu trên.
2. Chờ Unity import tài nguyên và resolve package. Mở scene:
   `Assets/MediaPipeUnity/Samples/Scenes/Hand Landmark Detection/Hand Landmark Detection.unity`.
3. Trong **File → Build Profiles**, chọn **Android** làm nền tảng hoạt động.
4. Kiểm tra danh sách scene: `Hand Landmark Detection` đứng đầu; `Assets/Scenes/SampleScene.unity` là scene phụ đang được cấu hình trong repository.
5. Trong **XR Plug-in Management → Android**, sử dụng **ARCore** cho ứng dụng này. Chế độ kính hoạt động bên trong cùng ứng dụng AR.
6. Dùng **File → Build And Run** để build và cài lên điện thoại, hoặc dùng menu kiểm tra/build bên dưới.

### Build Release kèm kiểm tra

Trong Unity, chọn **CircuitAR → Validate and Build Release APK**. Lệnh này kiểm tra cấu hình Android/ARCore, chạy bộ regression checks rồi tạo:

```text
Builds/CircuitAR-release.apk
```

**CircuitAR → Validate and Build Instant Placement APK** tạo bản Development để gỡ lỗi. Khi đánh giá độ mượt trên điện thoại, dùng bản Release; không bật Development Build hoặc Script Debugging.

Các file APK, `Library/`, `Logs/` và `Builds/` là sản phẩm sinh ra trên máy, được bỏ qua bởi Git.

### Chạy kiểm tra bằng dòng lệnh

Đóng Unity Editor đang mở cùng thư mục dự án trước khi chạy batch mode. Ví dụ PowerShell trên Windows; thay đường dẫn Unity nếu máy cài ở vị trí khác:

```powershell
$unity = 'C:/Program Files/Unity Hub/6000.3.24f1/Editor/Unity.exe'
& $unity -batchmode -quit -buildTarget Android -projectPath $PWD.Path -executeMethod ARPlacementRegressionChecks.Run -logFile './placement-checks.log'
```

Để chạy kiểm tra rồi build Release, thay `ARPlacementRegressionChecks.Run` bằng `ARInstantPlacementBuild.RunRelease`.

Báo cáo kiểm tra nằm trong `Logs/ARPlacementRegressionChecks.txt`; báo cáo build nằm trong `Logs/InstantPlacementBuild.txt`. Các kiểm tra Editor mô phỏng dữ liệu tracking và cử chỉ, không đo FPS hay xác nhận độ bám trên điện thoại thật.

## Cách thao tác

### Tự lắp mạch bằng AR

1. Cho phép truy cập camera và chọn **Tự lắp mạch**.
2. Mở kho, chọn linh kiện, đưa con trỏ tới vị trí cần đặt rồi pinch để xác nhận; chế độ AR cũng hỗ trợ chạm màn hình.
3. Pinch đúng thân linh kiện để kéo. Nhả ngón để dừng di chuyển; bấm vùng trống để bỏ chọn. Sử dụng các nút xoay/xóa của linh kiện đang chọn.
4. Bật nối dây, pinch cực thứ nhất rồi cực thứ hai. Cực đang chọn được tô vàng. Chuyển sang xóa dây khi cần tháo kết nối.
5. Bật mô phỏng và thao tác công tắc để kiểm tra mạch. Dùng hoàn tác/làm lại để sửa thao tác.

Pinch là chụm ngón cái và ngón trỏ trên **cùng một bàn tay**. Con trỏ xanh báo đang nhận cử chỉ pinch; màu xanh của linh kiện báo lựa chọn/thao tác trên linh kiện.

### Chế độ kính điện thoại

- Đặt điện thoại nằm ngang trong kính; mở chức năng kính từ menu chính.
- Đưa tay vào vùng camera nhìn thấy, mở ngón rồi pinch. Có thể dùng mỗi tay cho một linh kiện hoặc một tay giữ vật, tay còn lại thao tác giao diện/nối dây.
- **Kho linh kiện** và **Dây** chuyển trang trong cùng menu. Sau khi chọn linh kiện, nhả pinch của nút kho rồi pinch lại ở vị trí cần đặt; thao tác bấm nút kho không tự đặt vật ngay sát nút.
- Di chuyển menu bằng thanh kéo. Chọn linh kiện để hiện thanh xoay/xóa; giữ nút xóa để xác nhận.
- Mở **Chỉnh kính** để đưa hai ảnh gần/xa nhau cho phù hợp vị trí nhìn. Giá trị chỉnh ảnh được giữ lại cho lần mở ứng dụng sau.

**Cơ chế hiển thị:** màn hình được chia đôi theo dạng side-by-side. Cấu hình mặc định đồng bộ vật ảo theo camera đơn của điện thoại; hai mắt chia sẻ lượt render khi dùng cùng góc nhìn. Video thế giới thật là ảnh đơn nhãn, không phải video lập thể từ hai camera. Đây là chế độ kính gắn điện thoại, không có bộ điều khiển hay hệ tracking của kính độc lập.

## Đặt vật và độ ổn định

Đặt tức thì cho phép xác nhận một vị trí ước lượng trước khi AR đo xong mặt đỡ. Sau khi xác nhận, vật giữ tọa độ thế giới; bước dò mặt đỡ và tạo neo chạy nền. Vị trí ước lượng chưa phải bằng chứng vật đã bám chính xác vào mặt bàn thật.

Chỉ chuyển sang neo khi có mặt đỡ thật được xác nhận. Việc tinh chỉnh sử dụng điểm đặt đã lưu, không lấy lại một pixel cố định trên màn hình khi camera di chuyển. Các cập nhật nhỏ, riêng lẻ của neo không được tích lũy thành chuyển động kéo vật theo camera.

Khi tracking bị gián đoạn hoặc neo bị loại bỏ, ứng dụng giữ vị trí hợp lệ cuối cùng cùng các tham chiếu linh kiện/Undo. Việc phục hồi phải có bằng chứng nhất quán từ mặt đỡ; các thay đổi hợp lệ chung của cả mặt phẳng và neo vẫn được xử lý.

Độ chính xác vật lý vẫn phụ thuộc vào thiết bị, ánh sáng, chi tiết bề mặt và khả năng khôi phục tracking của ARCore. Bộ kiểm tra Editor không thay thế việc thử trên điện thoại và kính thật.

## Kiểm tra thủ công trước khi chia sẻ bản build

1. Đặt vật, nhả pinch hoàn toàn, lùi camera khoảng 0,5–1 m và đổi góc nhìn: vật giữ điểm tiếp xúc đã chọn, không đi theo camera.
2. Lặp lại trong chế độ kính, với một tay và hai tay; kiểm tra cả vật vừa đặt và vật đã kéo sang vị trí khác.
3. Quay camera đi vài giây rồi quay lại: linh kiện không bị xóa khi mất neo, không tự nhảy lên một mặt đỡ khác.
4. Kiểm tra pinch cực nhỏ, nối/xóa dây, mô phỏng và hoàn tác/làm lại sau khi phục hồi tracking.
5. Thoát/mở lại ứng dụng để kiểm tra lưu thông số chỉnh kính. Đánh giá FPS bằng cùng một mạch và bản Release trên thiết bị thật.

## Cấu trúc mã chính

| Đường dẫn | Vai trò |
| --- | --- |
| `Assets/Scripts/TapToPlaceController.cs` | Đặt, chọn, kéo, lịch sử và liên kết mặt đỡ |
| `Assets/Scripts/ARInstantPlacementAnchor.cs` | Giữ điểm đặt tức thì, xác nhận mặt đỡ và tạo neo nền |
| `Assets/Scripts/ARPlacedObjectStabilizer.cs` | Lọc rung và kiểm tra vị trí phục hồi |
| `Assets/Scripts/ButtonInteractor.cs` | Nhận pinch, con trỏ và danh tính hai tay |
| `Assets/Scripts/PhoneHeadsetAR.cs` | Menu và thao tác trong chế độ kính |
| `Assets/Scripts/PhoneHeadsetStereo.cs` | Khung hình cho hai ô mắt |
| `Assets/Scripts/WireConnectionController.cs` | Chọn cực, nối dây và cập nhật mesh dây |
| `Assets/Scripts/Circuit/` | Linh kiện và mô phỏng mạch |
| `Assets/Editor/*RegressionChecks.cs` | Các kiểm tra Editor |

## Một số lỗi build/cài đặt

- **Build ra ứng dụng/scene thử nghiệm khác:** kiểm tra Android profile đang hoạt động, scene đầu tiên và Application ID. Menu build Release có kiểm tra ARCore và chặn package mang hậu tố `.vrprototype`.
- **Build thành công nhưng báo `INSTALL_FAILED_USER_RESTRICTED`:** điện thoại đã chặn hoặc hủy cài đặt. Mở khóa máy, kiểm tra quyền cài qua USB và xác nhận hộp thoại trên điện thoại.
- **Không cài đè được:** kiểm tra package ID và chữ ký có trùng với bản đang cài. Giữ cùng chữ ký để cập nhật ứng dụng và giữ dữ liệu chỉnh kính.
- **FPS không đều:** so sánh bằng bản Release trên cùng thiết bị/mạch. Các tối ưu hiện có gồm dùng chung lượt render khi hai mắt cùng góc nhìn, bỏ dựng lại mesh dây đứng yên và tái sử dụng bộ nhớ dây.

## Thành phần bên thứ ba

Dự án sử dụng [MediaPipe Unity Plugin](https://github.com/homuler/MediaPipeUnityPlugin). Giấy phép của package được giữ tại [Packages/com.github.homuler.mediapipe/LICENSE.md](Packages/com.github.homuler.mediapipe/LICENSE.md). Các package Unity và tài nguyên bên thứ ba giữ giấy phép tương ứng của chúng.
