# Đặt linh kiện AR ngay

Nhánh: `feat/instant-ar-placement`, tạo từ `feat/real-world-surface-placement`.

## Cách dùng

1. Vào AR và chọn linh kiện. Không cần quét bàn/sàn trước.
2. Chạm vào vị trí muốn đặt trên camera, hoặc đưa tay vào khung hình rồi pinch.
3. Mở tay sau khi đặt. Pinch giữ vào linh kiện để kéo, mở tay để thả.

AR vẫn cần khởi động theo dõi vị trí camera. Khi chưa đo được bề mặt, vị trí đặt là ước lượng, không phải cam kết đã chạm đúng mặt bàn/sàn. HUD phân biệt hai trường hợp này. Khi có mặt thật, ứng dụng ưu tiên điểm đo; với vật vừa đặt ước lượng, tracked raycast có thể hiệu chỉnh trong tối đa 5 giây. Kéo vật sẽ dừng hiệu chỉnh đó. Neo native được tạo trong nền, không chặn xác nhận đặt vật.

## Thay đổi chính

- `enableInstantPlacement` và `enableTapToPlace` mặc định bật trên controller. Tắt `enableInstantPlacement` để dùng lại chính sách chỉ đặt lên mặt đo được.
- Chạm UI, vuốt, giữ quá lâu hoặc nhiều ngón không đặt linh kiện. Lệnh được giữ ở điểm đã chọn trong thời gian chống bấm lặp.
- Chỉ vật đặt ước lượng được kéo trên cao độ ước lượng khi chưa có mặt đo được. Vật đã có mặt thật giữ quy tắc hỗ trợ bề mặt cũ.
- Kiểm tra chồng lấn dùng danh sách lịch sử và footprint được cache, không quét scene hay cấp phát mảng mỗi khung hình. Undo/Redo và Reset dọn cả support đang chờ neo.
- Con trỏ nhận ngay chuyển động lớn, lọc rung nhỏ; pinch xác nhận theo các kết quả inference khác nhau. Kết quả quá cũ tính theo ảnh nguồn bị bỏ qua.
- AR không vẽ thêm skeleton mẫu của MediaPipe. Camera/inference vẫn chạy bất đồng bộ; giao diện Android yêu cầu 60 FPS, tách khỏi nhịp camera AR.

## Kiểm tra

Menu Unity: **CircuitAR → Validate AR Placement**.

Build APK kèm kiểm tra: **CircuitAR → Validate and Build Instant Placement APK**.

Đầu ra: `Builds/CircuitAR-instant-placement.apk`; báo cáo ở `Logs/ARPlacementRegressionChecks.txt` và `Logs/InstantPlacementBuild.txt`.

Kiểm tra Editor bao gồm đặt ngay không có plane/native manager, vị trí không đi theo camera, chạm trong cooldown, quyền sở hữu UI, Undo/Redo, kéo vật ước lượng, va chạm và hiệu chỉnh, tuổi kết quả inference, timestamp, render policy và cache không cấp phát khi đã khởi tạo. Các callback vòng đời trong fixture Editor được gọi tường minh; các kiểm tra này không thay thế kiểm thử ARCore trên thiết bị.

Khi thử trên Android, kiểm tra thêm: đặt ở vùng ít chi tiết, di chuyển camera sau đặt, đưa/rút tay nhanh, pinch trên UI rồi ra ngoài, nối dây trước khi hiệu chỉnh mặt thật, Undo rồi Reset khi neo còn chờ, đưa ứng dụng về nền rồi quay lại. Đo FPS/độ trễ bằng profiler trên máy đích.

## Mã tham khảo

- [FurnitureAR InteractionController](https://github.com/Shubhra22/FurnitureAR/blob/master/Assets/Scripts/InteractionController.cs): tâm ngắm và xác nhận đặt; vẫn cần polygon mặt phẳng.
- [Soonsoon PlaceObject](https://github.com/soonsoon2/SoonsoonPublicRes/blob/master/Assets/SamepleProject/ClassificationPlane/Script/PlaceObject.cs): tách preview và xác nhận; vẫn cần mặt phẳng phù hợp.
- [Soonsoon ARDrawLine](https://github.com/soonsoon2/SoonsoonPublicRes/blob/master/Assets/SamepleProject/AR%20Drawing/Script/ARDrawLine.cs): dùng điểm trước camera mà không đợi mặt phẳng. Đây là vẽ trong không gian, không tự đo mặt bàn.

Các source được dùng để tham khảo luồng tương tác; cơ chế đặt ước lượng và quản lý neo được triển khai cho kiến trúc hiện tại.
