# TÀI LIỆU CHI TIẾT CÁC SCRIPT (SCRIPT DOCUMENTATION)
**Dự án:** Game AR Monster Hunter  
**Cập nhật lần cuối:** 19/11/2026

---

## 1. BẢNG DANH MỤC SCRIPT HIỆN CÓ TRONG DỰ ÁN

| Tên Script | Chức năng chính | GameObject gắn vào | Trạng thái |
| :--- | :--- | :--- | :---: |
| `PlaceObjectOnPlane.cs` | Bắn Raycast xuống mặt phẳng AR và đặt quái vật (Scale 50x ~ 1.3m, hỗ trợ Touch & PC Click/Space) | `XR Origin` | ✅ Hoàn thành |
| `MonsterController.cs` | Máy trạng thái AI quái vật (FSM: Idle, Chase, Telegraph, Attack, Hit, Die) | Monster Prefab | ✅ Hoàn thành |
| `MonsterSetupTool.cs` | Công cụ Editor 1-click tự động tạo Animator, Monster Slime (Scale 50x) & Bat cho Level 2+ | Thanh Menu Unity | ✅ Hoàn thành |
| `HandLandmarkData.cs` | Định danh 21 khớp tay MediaPipe, tính Pinch Distance và các enum cử chỉ | Class dữ liệu | ✅ Hoàn thành |
| `GestureRecognizer.cs` | Hiện thực hóa 4 quy tắc cử chỉ: GRAB (Pinch), RELEASE (khi đang giữ), SHIELD (giơ cao), PUSH (lao tới) | `XR Origin` | ✅ Hoàn thành |
| `ARCameraStreamBridge.cs` | Cầu nối camera Android + Tự động tạo 3D Background Quad chiếu Webcam trên PC | `XR Origin` | ✅ Hoàn thành |
| `MediaPipeHandTracker.cs` | Bộ lọc chống nhầm khuôn mặt/cơ thể, trích xuất 21 landmarks YCrCb, banner trạng thái tinh gọn | `XR Origin` | ✅ Hoàn thành |
| `PlayerCombatController.cs` | Quản lý máu người chơi, Khiên 2s, Đẩy lùi 2m, Tụ cầu năng lượng & Đòn ném | `XR Origin` | ✅ Hoàn thành |
| `GameHUDController.cs` | Giao diện HUD chuẩn Mockup (Tim + Máu + Score, Level 1-5, X/30, Timer, Crosshair, 3 Nút kỹ năng) | `XR Origin` | ✅ Hoàn thành |
| `MonsterSpawner.cs` | Tự động sinh quái vật theo 5 Cấp độ (5s -> 1s), hỗ trợ Dơi bay Level 2+, Scale 50x | `XR Origin` | ✅ Hoàn thành |
| `CollectibleHeart.cs` | Trái tim hồi máu 3D phát quang URP (Hiệu ứng nhịp đập tim, bay về phía người chơi, hồi +30 HP) | Tự động sinh trong Scene | ✅ Hoàn thành |

---

## 2. HƯỚNG DẪN CHI TIẾT CÁC SCRIPT QUAN TRỌNG

---

### A. Script: `GestureRecognizer.cs` (4 Quy Tắc Cử Chỉ Đề Tài)
- **Dùng để làm gì:** Tiếp nhận dữ liệu 21 điểm mốc của bàn tay từ `MediaPipeHandTracker` và phân tích chính xác 4 cử chỉ:
  1. **GRAB (Cầm / Nắm):**
     - Chỉ kích hoạt khi khoảng cách giữa đầu ngón cái và đầu ngón trỏ đủ nhỏ: `currentPinchDistance <= pinchDistanceThreshold` (0.085).
     - Thiết lập cờ trạng thái `isHoldingObject = true`.
  2. **RELEASE (Thả / Ném):**
     - **Chỉ có tác dụng khi trước đó người chơi đang giữ vật thể** (`isHoldingObject == true`).
     - Khi người chơi mở ngón tay ra (`currentPinchDistance > 0.14f`), kích hoạt đòn ném tấn công và reset `isHoldingObject = false`.
     - Nếu chưa giữ vật thể, việc mở tay hoàn toàn không kích hoạt đòn ném.
  3. **SHIELD (Tường chắn bảo vệ):**
     - Cần giữ bàn tay mở ở vị trí **GIƠ CAO** (`wrist.y > 0.35f` hoặc `handCenterY > 0.48f`).
     - Bàn tay mở nhưng hạ thấp ở ngực dưới/bụng sẽ bị bỏ qua, không kích hoạt khiên.
  4. **PUSH (Trường lực đẩy):**
     - Phát hiện chuyển động mở tay lao nhanh về phía trước camera (`spanVelocity > 0.35f` hoặc `zVelocity > 1.2f`).
     - Không nhận diện nếu chỉ xòe tay đứng yên mà phải có lực đẩy cơ học thực tế.
- **Hỗ trợ phím test trên PC:**
  - Phím `1` = Nắm (GRAB) | Phím `2` = Khiên giơ cao (SHIELD) | Phím `3` = Đẩy (PUSH) | Phím `4` = Thả ném (RELEASE).

---

### B. Script: `MediaPipeHandTracker.cs` (Thị Giác Máy Tính 21 Khớp & Bộ Lọc Cơ Thể)
- **Dùng để làm gì:**
  - Nhận luồng ảnh từ `ARCameraStreamBridge`.
  - Phân tích màu da theo không gian màu `YCrCb` chống nhiễu ánh sáng đèn phòng huỳnh quang/LED.
  - **Bộ lọc loại trừ toàn thân:** Tự động phát hiện khi người chơi chỉ ngồi trước camera (da chiếm > 65% khung hình), tránh nhận diện nhầm khuôn mặt và áo thành bàn tay khổng lồ.
  - Tắt vẽ đè các đường xương toàn màn hình (`showSkeletonOnMainScreen = false`) để tầm nhìn chiến đấu trong suốt, sắc nét.
  - Quét 5 cột tìm đỉnh các ngón tay thực tế, tính khoảng cách ngón cái - ngón trỏ thời gian thực.
  - Hiển thị banner trạng thái tinh gọn ở đáy màn hình.

---

### C. Script: `ARCameraStreamBridge.cs` (Nền 3D Webcam Không Bị Che Quái)
- **Dùng để làm gì:**
  - Trên PC: Tự động khởi chạy Webcam máy tính và tạo một tấm `PC_WebCam_Background_Quad` ở cự ly `Z = 12m` gắn theo Camera. Mọi quái vật 3D ở cự ly 2.0m sẽ đứng **nổi bật phía trước căn phòng thật**, không còn bị che khuất bởi giao diện `OnGUI`.
  - Trên Android: Sử dụng trực tiếp hàm `ARCameraManager.TryAcquireLatestCpuImage` để lấy luồng ảnh CPU từ ARCore mà không xung đột phần cứng.

---

### D. Script: `MonsterSpawner.cs` (Sinh Quái Theo 5 Cấp Độ)
- **Dùng để làm gì:**
  - Tự động sinh quái vật theo 5 Cấp độ: Level 1 (5s/quái), Level 2 (4s/quái), Level 3 (3s/quái), Level 4 (2s/quái), Level 5 (1s/quái).
  - Tỷ lệ kích thước quái vật **50.0x** (chiều cao thực tế $\approx 1.3\text{ m}$), quái vật to lớn, oai phong, hầm hố và thấy rõ trọn vẹn toàn thân.
  - Hỗ trợ Dơi Tím Bat (`Bat_Violet`) bay trên không bắt đầu từ Level 2+.
  - Đặt độ cao ngang tầm mắt (`camY - 0.65m` hoặc mặt sàn $y = 0\text{ m}$) ở khoảng cách 2.0m ngay trước mặt người chơi.


---

## 5. CẬP NHẬT TỔNG THỂ HỆ THỐNG CHIẾN ĐẤU & CỬ CHỈ (20/11/2026)

### 5.1. `GestureRecognizer.cs`
- **Chức năng:** Nhận diện 3 cử chỉ cốt lõi:
  * `Slash` (Chém quái): Tính toán vận tốc tâm bàn tay `(centroid - prevCentroid) / dt`. Khi vận tốc vượt ngưỡng 0.70 m/s, phát sự kiện `HandGestureType.Slash` sau 0.05s.
  * `OpenPalm` (Tường chắn): Kiểm tra 5 ngón tay mở thẳng và giơ cao ngang ngực/mặt. Kích hoạt `HandGestureType.OpenPalm`.
  * `Grab` (Lấy vật phẩm): Phát hiện chụm cả bàn tay lại (`isAllFingersCurled || pinch <= 0.10`). Kích hoạt `HandGestureType.Grab`.
- **Phím test PC:** Hỗ trợ phím 1/Click trái (Chém), phím 2 (Khiên), phím 3/Click phải (Lấy vật phẩm), phím 4 (Đẩy lùi).

### 5.2. `PlayerCombatController.cs`
- **Chức năng:**
  * `ExecuteSlashAttack()`: Quét Raycast và quét góc 60 độ phía trước camera trong cự ly 4.0m, gây 45 sát thương cho quái vật gần nhất, kích hoạt vệt chém kiếm khí trên HUD.
  * `ActivateShield()`: Duy trì lá chắn trong 2.5s, miễn nhiễm 100% sát thương từ quái vật.
  * `ExecuteGrabItem()`: Tăng số lượng vật phẩm nhặt được, cộng 50 điểm thưởng và kích hoạt hiệu ứng hào quang vàng.

### 5.3. `GameHUDController.cs`
- **Chức năng:**
  * 3 nút kỹ năng dưới góc trái: `✊ Lấy đồ`, `🛡️ Tường chắn`, `⚔️ Chém quái`.
  * Hiệu ứng vệt chém kiếm khí Neon `DrawSlashBladeEffect()` kéo dài 0.35s với độ mờ fade out mượt mà.
  * Hiệu ứng hào quang vàng nhặt vật phẩm `DrawItemPickupBanner()`.
  * Hiệu ứng vòm tường chắn năng lượng `DrawShieldArcEffect()` bên trái màn hình.

### 5.4. `MonsterSpawner.cs` & `MonsterController.cs`
- **Chức năng:**
  * Quái vật Slime (mặt đất) và Bat (bay trên không) xuất hiện cách người chơi 2.4m, chiều cao ~1.3m (scale 50x) ngay trong tầm ngắm.
  * Quái vật di chuyển tốc độ 0.85 m/s đến cự ly 1.25m, dừng lại 1.5s (Telegraph) để người chơi kịp phản xạ trước khi tung đòn đánh.
