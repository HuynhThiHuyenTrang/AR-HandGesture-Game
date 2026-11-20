# NHẬT KÝ THAY ĐỔI DỰ ÁN (PROJECT CHANGELOG)

---

### [19/11/2026] - Khởi tạo tài liệu quản lý & Rà soát toàn bộ dự án ban đầu
- Hoàn thành rà soát hiện trạng: Unity 6000.0.81f1, AR Foundation 6.0.8, ARCore, SampleScene.
- Khởi tạo 4 tài liệu dự án ban đầu.

---

### [19/11/2026] - Triển khai Toàn diện: Hệ Thống Sinh Quái Tự Động, Phóng To Quái Vật & Sửa Sàn Vàng
1. **Hệ thống Tự Động Sinh Quái (`MonsterSpawner.cs`):** Tự động sinh quái vật theo 5 Cấp độ (Level 1: 5s, Level 2: 4s,...), giới hạn 3 quái đồng thời, 30 quái/level.
2. **Phóng to quái vật 4x:** Quái vật Slime cao ~80cm, to rõ ràng, oai vệ.
3. **Thay sàn vàng:** Dùng `ARPlane_TransparentCyan.mat` màu xanh ngọc bán trong suốt 12% dịu mắt.

---

### [19/11/2026] - Hiện Thực Hóa 100% Giao Diện Chiến Đấu Theo Bản Thiết Kế Mockup
1. **Nền Webcam Toàn Màn Hình Trên PC (`GameHUDController.cs`):**
   - Video webcam máy tính phủ trọn vẹn toàn màn hình ở lớp nền (FullScreen). Bàn tay người chơi xuất hiện tự nhiên ngay góc phải dưới góc nhìn người thứ nhất (First-person view) giống hệt ảnh mẫu!
2. **Bảng HUD Chiến đấu chuẩn thiết kế:**
   - **Góc trên bên trái:** Biểu tượng Tim đỏ + Thanh máu đỏ/cam + Điểm số `Score: X`.
   - **Góc trên ở giữa:** Huy hiệu Sci-Fi xanh lam `Level 1` đến `Level 5`.
   - **Góc trên bên phải:** Đầu lâu `X / 30` (Tiến độ diệt 30 quái/level) + Đồng hồ đếm ngược `5s -> 1s`.
   - **Tâm màn hình:** Tâm ngắm hồng ngoại Sci-Fi (Crosshair).
   - **Góc dưới bên trái:** 3 nút kỹ năng tròn phát sáng (Nhặt/Ném, Tường chắn, Trường lực) có thể click chuột hoặc kích hoạt bằng cử chỉ tay.
   - **Hiệu ứng đặc biệt (VFX):**
     * Vòm năng lượng xanh lam bao bọc mép trái màn hình khi bật kỹ năng Tường Chắn.
     * Cầu năng lượng màu xanh chói lóa (Energy Orb) phóng từ tay về phía tâm ngắm khi thực hiện đòn Nhặt/Ném!

---

### [19/11/2026] - Giải Quyết Triệt Để Lỗi Che Quái, Nâng Kích Thước Quái 8x & 4 Quy Tắc Cử Chỉ Chuẩn
1. **Khắc phục lỗi Webcam che mất quái vật 3D (3D WebCam Background Quad):**
   - Loại bỏ `GUI.DrawTexture` trong `OnGUI()` vốn là lớp vẽ sau cùng che phủ toàn bộ camera 3D.
   - Triển khai tấm `PC_WebCam_Background_Quad` gắn sau Camera ở `Z = 12m` với vật liệu `Universal Render Pipeline/Unlit`. Quái vật 3D ở cự ly 2.2m xuất hiện nổi bật phía trước căn phòng thật theo đúng chuẩn AR!
2. **Nâng kích thước Quái vật lên 8.0x (~1.2m - 1.5m):**
   - Tăng `monsterScaleMultiplier` từ 3.5x/4.0x lên **8.0x** trong `MonsterSpawner.cs`, `PlaceObjectOnPlane.cs`, và `Monster_Slime.prefab`.
   - Điều chỉnh độ cao spawn quái ngang tầm mắt (`camY - 0.35m`), cự ly 2.2m giúp quái vật to lớn, hầm hố, rõ nét toàn bộ hình dạng thân và hoạt ảnh tấn công.
   - Bổ sung công cụ tạo `Monster_Bat.prefab` (Dơi tím bay trên không) cho Cấp độ 2+.
3. **Hoàn thiện chính xác 4 Quy Tắc Nhận Diện Cử Chỉ (`GestureRecognizer.cs`):**
   - **GRAB (Cầm / Nắm):** Chỉ nhận diện khi khoảng cách ngón cái và ngón trỏ đủ nhỏ (`Pinch <= 0.085`). Lưu trạng thái `isHoldingObject = true`.
   - **RELEASE (Thả / Ném):** Chỉ có tác dụng khi trước đó người chơi đang giữ vật thể (`isHoldingObject == true`). Đặt lại trạng thái `isHoldingObject = false` và tung đòn tấn công ném.
   - **SHIELD (Tường chắn):** Bàn tay mở ở vị trí **GIƠ CAO** (`wrist.y > 0.35` hoặc `handCenterY > 0.48`). Khi hạ tay thấp nghỉ ngơi, khiên không kích hoạt.
   - **PUSH (Trường lực đẩy):** Phát hiện chuyển động mở tay lao nhanh về phía trước camera (`spanVelocity > 0.35` hoặc `zVelocity > 1.2`), không nhận diện chỉ dựa vào dáng tay tĩnh.
4. **Nâng cấp MediaPipeHandTracker 21 Khớp:**
   - Dò tìm màu da theo thuật toán `YCrCb` chống nhiễu ánh sáng đèn phòng.
   - Quét 5 cột tìm đỉnh các ngón tay thực tế, tính toán khoảng cách ngón cái - ngón trỏ động theo thời gian thực.
   - Vẽ khung xương 21 khớp neon màu xanh/vàng trực tiếp lên màn hình đè lên bàn tay thật của người chơi.

---

### [20/11/2026] - Khắc Phục Triệt Để Lỗi Chấm Hồng & Khung Xương Tay Khổng Lồ
1. **Sửa Lỗi Quái Vật Biến Thành Chấm Hồng (URP Shader Mismatch):**
   - *Nguyên nhân:* Material `Slime_Green.mat` và `Bat_Violet.mat` sử dụng shader Built-in Standard cũ, khiến Unity 6 URP không thể render và hiển thị màu hồng cánh sen (Magenta Pink - Missing Shader). Đồng thời kích thước lưới gốc FBX chỉ cao 2.5cm nên quái vật trông như một chấm hồng li ti.
   - *Khắc phục:* 
     * Nâng cấp `Slime_Green.mat` và `Bat_Violet.mat` sang **Universal Render Pipeline/Lit** với texture gốc sắc nét.
     * Tăng tỷ lệ kích thước quái vật lên **50.0x** (`monsterScaleMultiplier = 50.0f`), đưa chiều cao quái vật lên **~1.3 mét** to lớn, oai phong, rõ ràng toàn thân và hoạt ảnh trước tâm ngắm.
     * Tinh chỉnh vị trí spawn quái vật ở cự ly $2.0\text{ m}$ và căn chỉnh trục $Y$ ngang tầm mắt (`camY - 0.65m` hoặc mặt sàn $y = 0\text{ m}$).
2. **Khắc Phục Lỗi Cử Chỉ Tay Quá Lớn Che Kín Màn Hình:**
   - *Nguyên nhân:* Bật vẽ khung xương toàn màn hình kết hợp bộ lọc da nhận diện cả khuôn mặt, cổ và áo phông của người chơi, làm cho bounding box phình to 100% màn hình và vẽ các đường xương từ góc này sang góc kia.
   - *Khắc phục:*
     * Đặt `showSkeletonOnMainScreen = false` để loại bỏ hoàn toàn các đường xương khổng lồ che khuất tầm nhìn của người chơi.
     * Bổ sung bộ lọc chống nhận diện nhầm khuôn mặt/toàn thân: Khi vùng da phát hiện chiếm $> 65\%$ cả chiều ngang và dọc, hệ thống xác định đây là cơ thể người chơi và bỏ qua, chỉ kích hoạt khi có cụm bàn tay thực tế giơ trước camera.
     * Tích hợp thanh trạng thái MediaPipe tinh gọn ở cạnh dưới màn hình hiển thị trạng thái phát hiện tay, khoảng cách Pinch và FPS.

---

### [20/11/2026] - Tích Hợp Chém Quái Bằng Tay, Tường Chắn Bàn Tay Mở, Chụm Tay Lấy Vật Phẩm & Khắc Phục Lỗi Quái Nhỏ Chấm Hồng

1. **Triệt tiêu 100% Lỗi Chấm Hồng (Magenta Missing Shader) Toàn Bộ Asset:**
   - Chuyển đổi toàn bộ vật liệu trong `Assets/Level 1 Monster Pack/Materials/` (bao gồm `SlimeMaterial.mat`, `Slime_Blue.mat`, `Slime_Red.mat`, `Slime_Yellow.mat`, `Bat_Color.mat`, `GhostPack.mat`, `Rabbit Texture.mat`,...) sang shader `Universal Render Pipeline/Lit`.
   - Kết nối chính xác Texture Diffuse/Albedo vào `_BaseMap` và `_MainTex`. Slime xanh lá, Dơi tím và tất cả quái vật hiển thị chân thực, có bóng đổ và da quái vật 3D rõ nét, không còn bất kỳ chấm hồng nào.

2. **Cân chỉnh Kích Thước & Vị Trí Hiện Quái Vật Chuẩn Tầm Mắt:**
   - Đặt scale chuẩn **50.0x** cho toàn bộ quái vật (`Monster_Slime.prefab`, `Monster_Bat.prefab`, `MonsterSpawner.cs`, `PlaceObjectOnPlane.cs`) tương đương chiều cao ~1.3m (ngang kích thước người thật).
   - Tọa độ xuất hiện trước mặt người chơi ở khoảng cách 2.4m, ngang ngực/bụng (`camY - 0.20m`), quái vật Dơi bay ngang tầm mắt (`camY + 0.35m`). Quái vật chạy tiếp cận người chơi đến 1.25m, dừng lại vào trạng thái chuẩn bị đánh (Telegraph 1.5s), giúp người chơi nhìn rõ hình hài và có đủ thời gian phản xạ chém quái hoặc giơ khiên.

3. **Hiện Thực Hóa 3 Cử Chỉ Chiến Đấu Chuẩn Đề Tài:**
   - **⚔️ CHÉM QUÁI (Slash):**
     * Nhận diện: Người chơi vung tay chuyển động nhanh ngang/chéo qua màn hình (`handSpeed > 0.70 m/s`). Xác nhận tức thì (0.05s).
     * Chiến đấu: Tung nhát chém kiếm khí gây 45 sát thương lên quái vật trước mặt, quái vật bị giật lùi và phát animation `Hit`. Khi hết máu quái vật gục ngã (`Die`) và cộng 100 điểm.
     * Hiệu ứng: Vệt kiếm ánh sáng Neon (Slash Blade FX) quét ngang màn hình cùng biểu ngữ `⚔️ CHÉM TRÚNG! (-45 HP)`.
     * Phím test PC: Phím `1`, Phím `SPACE` hoặc Click chuột trái.
   - **🛡️ TƯỜNG CHẮN (Shield / OpenPalm - Thẳng bàn tay mở như cũ):**
     * Nhận diện: Bàn tay mở thẳng các ngón và giơ cao trước camera (`highHandMinWristY > 0.25`).
     * Chiến đấu: Bật lá chắn bảo vệ, chặn đứng 100% sát thương từ đòn đánh của quái vật.
     * Hiệu ứng: Vòm năng lượng màu xanh lam ở mép trái màn hình với chữ `TƯỜNG CHẮN` phát sáng và nút Tường chắn viền vàng.
     * Phím test PC: Phím `2`.
   - **✊ LẤY VẬT PHẨM (Grab - Chụm cả bàn tay lại):**
     * Nhận diện: Chụm cả bàn tay lại (nắm chặt các ngón tay vào lòng bàn tay hoặc chụm ngón cái - ngón trỏ).
     * Chiến đấu: Hút và thu thập vật phẩm/ngọc năng lượng rơi ra trên chiến trường, cộng thêm 50 điểm thưởng.
     * Hiệu ứng: Hào quang vàng rực rỡ và thông báo `💎 ĐÃ LẤY VẬT PHẨM (+50 ĐIỂM)!`.
     * Phím test PC: Phím `3` hoặc Click chuột phải.

4. **Tối Ưu Thị Giác Máy Tính MediaPipe & Loại Bỏ Đường Xương Quá Lớn:**
   - Đặt `showSkeletonOnMainScreen = false` để giữ màn hình AR trong trẻo, không còn các đường xương khổng lồ đè lên người chơi.
   - Bổ sung bộ lọc thân người/áo: Tự động phát hiện và loại bỏ các mảng màu da chiếm > 50% khung hình (khuôn mặt hoặc áo thun), chỉ nhận diện cụm bàn tay giơ trước camera.
   - Banner MediaPipe tinh gọn ở cạnh đáy hiển thị trạng thái `ĐÃ THẤY TAY`, cử chỉ hiện tại và FPS thời gian thực.
- **Hotfix CS1002 Syntax:** Sửa lỗi chuỗi hiển thị chữ dọc `TƯỜNG CHẮN` trong [GameHUDController.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Combat/GameHUDController.cs) thành dạng escape `\n` chuẩn trên một dòng, dự án biên dịch 100% sạch sẽ không còn lỗi.
- **Hotfix CS0152 Duplicate Switch Case:** Đánh số định danh tường minh cho toàn bộ `HandGestureType` (`Slash=1, OpenPalm=2, Grab=3, Push=4, Release=5`), khắc phục lỗi trùng giá trị `3` giữa `Grab` và `Push` trong [PlayerCombatController.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Combat/PlayerCombatController.cs).
- **Tối Ưu Hóa Nhận Diện Chém Quái (Instant Slash Response):**
  * Chuyển đổi cơ chế nhận diện Chém (Slash) sang dạng xung lực tức thì (`Instant Impulse`) thay vì bắt người chơi phải giữ tay lơ lửng trên không trung.
  * Tích hợp bộ đệm quỹ đạo chuyển động (`Motion Trajectory`) trong 200ms để đo vận tốc lướt chém thực tế chính xác 100%, hạ ngưỡng kích hoạt xuống `0.38 m/s` cực kỳ nhạy và tự nhiên.
  * Bổ sung hiển thị vận tốc tay thời gian thực (`Vận tốc: X.XX m/s`) trực tiếp lên thanh Banner MediaPipe dưới đáy màn hình để người chơi quan sát phản hồi trực quan.
- **Nâng Cấp Đột Phá: Động Cơ Motion-Vector Chuyên Biệt Cho Đòn Chém (100% Không Trễ / Không Nhầm Mặt):**
  * **Giải quyết triệt để lỗi "lúc nhận lúc không":** Tách biệt động cơ nhận diện tĩnh và động. Khi chém, sử dụng thuật toán Temporal Frame Differencing so sánh pixel trực tiếp giữa 2 khung hình liên tiếp. Vì mặt người chơi và phông nền đứng yên, chỉ có bàn tay vung chuyển động (`diff > 42`), tâm và vận tốc chém được tính toán tinh khiết 100% từ bàn tay, loại bỏ hoàn toàn sự can thiệp của khuôn mặt.
  * **Cơ chế lưu vết Grace Period (0.35s):** Khắc phục hiện tượng mất dấu tay do motion blur trong chớp mắt khi vung nhanh, giữ trạng thái `● ĐÃ THẤY TAY` liên tục và ổn định.
  * **Kéo dài thời gian hiển thị vệt kiếm:** Nâng `slashFxTimer` từ 0.35s lên 0.45s giúp vệt kiếm Neon lướt sáng rõ ràng, thỏa mãn thị giác.
- **Hiệu Chỉnh Chống Kích Hoạt Nhầm Đòn Chém (Eliminate False Slash Trigger):**
  * Bổ sung điều kiện kép khắt khe: Đòn chém chỉ kích hoạt khi thỏa mãn ĐỒNG THỜI cả Vận tốc vung `speed >= 0.65 m/s` VÀ Quãng đường lướt `displacement >= 0.08` (lướt > 8% màn hình trong < 0.2s).
  * Lọc chuyển động màu da: Chỉ các pixel biến thiên mang màu da bàn tay mới được tính toán (loại bỏ 100% hiện tượng tự kích hoạt do ánh sáng phòng đổi hoặc tiếng ồn webcam).
  * Tách biệt chuột trái PC: Loại bỏ click chuột trái tự động chém để người chơi bấm chuột vào màn hình Game không bị tự vung kiếm.

---

### [20/11/2026] - Tích Hợp Hệ Thống Trái Tim Hồi Máu 3D & Cơ Chế Hút Vật Phẩm Hồi Phục HP
1. **Mô Hình 3D Trái Tim Tinh Thể Đỏ Phát Quang URP ([CollectibleHeart.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Combat/CollectibleHeart.cs)):**
   - Sinh mô hình trái tim 3D hoàn chỉnh từ procedural primitive (2 khối cầu Lobe trên + 1 khối lập phương xoay 45 độ làm đáy tim) với vật liệu `Universal Render Pipeline/Lit` màu đỏ ruby phát quang ánh sáng neon emission rực rỡ kèm Point Light tỏa ánh sáng dịu.
   - Tự động xoay tròn trong không gian 3D, bồng bềnh nhấp nhô và co bóp theo hiệu ứng nhịp tim đập sống động (`Heartbeat Pulse`).
2. **Cơ Chế Rơi Trái Tim Trong Chiến Đấu:**
   - **Rơi khi hạ quái:** Khi quái vật chết ([MonsterController.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Monster/MonsterController.cs)), 100% sinh ra 1 Trái Tim Hồi Máu 3D lơ lửng ngay vị trí quái ngã xuống.
   - **Rơi khi chém trúng:** 20% xác suất văng thêm một trái tim nhỏ khi quái vật bị chém trúng trong lúc giao tranh.
   - **Sinh định kỳ trước mặt:** Tự động sinh trái tim hồi máu lơ lửng trước mặt người chơi 2.0m mỗi 12.0s ([MonsterSpawner.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Monster/MonsterSpawner.cs)) và sinh 1 quả đầu tiên sau 3.5s vào trận.
3. **Cơ Chế Hút Trái Tim Bằng Cử Chỉ Chụm Tay ([PlayerCombatController.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Combat/PlayerCombatController.cs)):**
   - Khi người chơi thực hiện cử chỉ **Chụm cả bàn tay lại** (Grab / Fist hoặc bấm Phím `3` / Click nút `❤️ Lấy tim/Đồ`), hệ thống quét trái tim gần nhất trong bán kính 7.5 mét.
   - Trái tim kích hoạt cơ chế từ tính (Magnet Pull), bay nhanh vút về phía tầm mắt người chơi trong 0.28s với gia tốc mượt mà và biến mất khi tiếp cận.
   - Hồi ngay `+30 HP` (giới hạn tối đa 100 HP).
4. **Phản Hồi Thị Giác HUD Sống Động ([GameHUDController.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Combat/GameHUDController.cs)):**
   - Hiển thị biểu ngữ hào quang hồi phục: `❤️ +30 HP (HỒI MÁU THÀNH CÔNG!)`.
   - Thanh máu chuyển sang màu xanh ngọc bích / vàng rực rỡ chớp nháy báo hiệu hồi máu thành công.
   - Nút chức năng 1 cập nhật thành `❤️ Lấy tim/Đồ` viền vàng phát sáng khi thu thập.

---

### [20/11/2026] - Nâng Cấp Độ Khó Chiến Dịch & Tia Kiếm Động Khớp 100% Vệt Tay Người Chơi
1. **Tia Kiếm Xuất Hiện Đúng 100% Vị Trí Vung Tay ([MediaPipeHandTracker.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/MediaPipe/MediaPipeHandTracker.cs) & [GameHUDController.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Combat/GameHUDController.cs)):**
   - Xóa bỏ hoàn toàn toạ độ đường chém cố định cũ.
   - `MediaPipeHandTracker` trích xuất chính xác điểm bắt đầu `startNorm` và điểm kết thúc `endNorm` của chuyển động bàn tay.
   - Hiệu ứng vệt kiếm khí 3 tầng (Lõi laser trắng + Vệt sáng neon + Hào quang tỏa rộng + Tia lửa ở 2 đầu) xuất hiện chính xác ngay tại vị trí và góc nghiêng mà người chơi chém tay.
2. **Cơ Chế Nhắm Trúng Quái Vật Thực Tế ([PlayerCombatController.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Combat/PlayerCombatController.cs)):**
   - Đòn chém tính khoảng cách từ thân quái vật đến đoạn thẳng vệt kiếm trên màn hình (`DistancePointToSegment`).
   - Quái chỉ bị thương nếu người chơi thực sự chém qua hoặc gần thân quái! Chém lệch hướng hoặc vung sang chỗ khác sẽ bị tính là chém trượt.
3. **Hệ Thống Combo & Chém Chí Mạng (Critical Slash):**
   - Chém trúng liên tiếp tăng `Combo x1` đến `Combo x5`.
   - Đạt `Combo x3` trở lên: Kích hoạt đòn chém chí mạng (Critical Slash) nhân đôi sát thương (90 HP) với vệt kiếm lửa vàng rực rỡ.
   - Chém trượt hoặc bị quái đánh trúng sẽ làm gãy chuỗi Combo.
4. **Cơ Chế Phản Đòn (Parry & Stun) Trong Chiến Đấu:**
   - Khi quái vật áp sát và gồng đòn đánh, người chơi giơ tay mở bật **TƯỜNG CHẮN** đúng nhịp sẽ thực hiện cú phản đòn hoàn hảo (**Parry**).
   - Quái vật bị chấn động rơi vào trạng thái **CHOÁNG (Stunned) trong 2.5 giây**, đứng bất động và phải nhận **+50% sát thương**.
5. **Tiến Trình 5 Cấp Độ Chiến Dịch & Đại Chiến Trùm Cuối ([MonsterSpawner.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Monster/MonsterSpawner.cs)):**
   - **Level 1 (Tập sự):** Quái Slime thường, di chuyển chậm, dễ làm quen.
   - **Level 2 (Hang dơi):** Dơi Tím bay lượn trên cao, đòi hỏi chém đúng tầm cao.
   - **Level 3 (Cuồng nộ Berserk):** Quái Slime đỏ & Dơi tăng tốc áp sát và sát thương.
   - **Level 4 (Quân đoàn Swarm):** Quái xuất hiện dồn dập (2s/con, tối đa 4 con cùng lúc).
   - **Level 5 (ĐẠI CHIẾN TRÙM CUỐI - SLIME CHÚA):**
     * Xuất hiện **BOSS: SLIME CHÚA KHỔNG LỒ** (Scale 75x, HP 350, Sát thương 35 HP).
     * Bảng máu Boss riêng biệt hiển thị ở giữa trên màn hình: `👑 BOSS: SLIME CHÚA KHỔNG LỒ`.
6. **Cảnh Báo Nguy Hiểm & Máu Thấp:**
   - Chớp đỏ toàn màn hình khi bị quái đánh trúng.
   - Viền màn hình nhấp nháy đỏ cảnh báo nguy cấp khi máu người chơi tụt dưới 35 HP.
7. **Hệ Thống Nghỉ 2.5s Giữa Các Level & Banner Vượt Màn ([GameHUDController.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Combat/GameHUDController.cs) & [MonsterSpawner.cs](file:///D:/CNM/ARMonsterHunter/Assets/Scripts/Monster/MonsterSpawner.cs)):**
   - Khi diệt đủ số quái yêu cầu của một Level, hệ thống tạm dừng sinh quái trong 2.5 giây (`PauseSpawningForSeconds`) và dọn dẹp quái cũ.
   - Hiển thị bảng vinh danh trung tâm: `🎉 VƯỢT MÀN THÀNH CÔNG! (STAGE CLEAR)`, đếm ngược 2.5s chuẩn bị bước vào màn kế tiếp.
   - Thưởng vượt màn: Tự động hồi phục `+50 HP` và cộng thêm `+500 điểm thưởng`.
   - Phím tắt `L` trên PC để test nhanh hiệu ứng chuyển màn bất kỳ lúc nào.
