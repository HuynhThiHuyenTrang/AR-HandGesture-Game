# TIẾN ĐỘ DỰ ÁN (PROJECT PROGRESS)
**Dự án:** Game AR Monster Hunter điều khiển bằng cử chỉ bàn tay (MediaPipe + Unity + FastAPI + Adafruit IO)  
**Đường dẫn dự án:** `D:\CNM\ARMonsterHunter`  
**Ngày cập nhật:** 20/11/2026  
**Phiên bản Unity:** 6000.0.81f1 (Unity 6)

---

## 1. TỔNG QUAN TIẾN ĐỘ

| Giai đoạn | Nội dung | Trạng thái | Ghi chú |
| :--- | :--- | :---: | :--- |
| **Giai đoạn 1** | Cấu hình Unity & XR Plugins | ✅ Hoàn thành | Đã cài đặt đầy đủ package AR Foundation, ARCore, Simulation |
| **Giai đoạn 2** | AR Scene Setup (`SampleScene.unity`) | ✅ Hoàn thành | Đã gắn cố định toàn bộ hệ thống Component vào `XR Origin` |
| **Giai đoạn 3** | Chức năng Spawn Quái vật (Cơ chế 3 tầng) | ✅ Hoàn thành | Tự động sinh quái vật trước mắt ở khoảng cách 2.4m, scale 50x (~1.3m) |
| **Giai đoạn 4** | Khắc phục Triệt để Lỗi Chấm Hồng & Scale 50x | ✅ Hoàn thành | Đã chuyển đổi 100% material sang URP Lit, triệt tiêu lỗi shader tím/hồng |
| **Giai đoạn 5** | Tự động sinh quái vật theo 5 Cấp độ (`MonsterSpawner.cs`) | ✅ Hoàn thành | Level 1 (5s), Level 2 (4s), Level 3 (3s), Level 4 (2s), Level 5 (1s), 30 quái/level. Hỗ trợ Dơi Bat_Violet Level 2+ |
| **Giai đoạn 6** | Thay thế sàn vàng thành lưới Sci-Fi bán trong suốt | ✅ Hoàn thành | Đổi vật liệu `ARPlane.prefab` sang `ARPlane_TransparentCyan.mat` dịu mắt |
| **Giai đoạn 7** | Tải và tích hợp Model Google MediaPipe | ✅ Hoàn thành | Đã tải `hand_landmarker.task` (~7.82MB) vào `Assets/StreamingAssets/` |
| **Giai đoạn 8** | Tối Ưu MediaPipe & Tắt Khung Xương Đè Toàn Màn Hình | ✅ Hoàn thành | Bộ lọc thân người/áo chống nhận diện nhầm, tắt đường skeleton che quái, banner HUD tinh gọn |
| **Giai đoạn 9** | 3 Cử Chỉ Chiến Đấu Chuẩn Yêu Cầu (`GestureRecognizer.cs`) | ✅ Hoàn thành | 1. ⚔️ Chém quái (Vung tay nhanh); 2. 🛡️ Tường chắn (Thẳng bàn tay mở giơ cao); 3. ✊ Lấy vật phẩm (Chụm cả bàn tay lại) |
| **Giai đoạn 10** | Giao diện HUD Chiến đấu Chuẩn Thiết Kế (`GameHUDController.cs`) | ✅ Hoàn thành | Tim đỏ + Thanh máu + Score, Huy hiệu Level 1-5, Đầu lâu (X/30), Đồng hồ, 3 Nút kỹ năng (Lấy đồ, Tường chắn, Chém quái), Vệt kiếm chém Neon FX & Hào quang vật phẩm FX |
| **Giai đoạn 11** | Hệ thống Trái Tim Hồi Máu 3D & Hút Vật Phẩm (`CollectibleHeart.cs`) | ✅ Hoàn thành | Sinh trái tim 3D đỏ phát quang URP (đập nhịp đập tim, lơ lửng, rơi khi hạ quái hoặc spawn định kỳ). Cử chỉ chụm tay hút tim bay vào màn hình hồi +30 HP |
| **Giai đoạn 12** | Nâng Cấp Chiến Dịch, Tia Chém 1:1 Theo Tay & Boss Slime Chúa | ✅ Hoàn thành | Tia chém xuất hiện 100% khớp vệt tay vung; nhắm trúng quái mới dính đòn; Combo x5; Phản đòn Parry choáng quái 2.5s (+50% DMG); Trùm Cuối Slime Chúa Level 5 (HP 350) |
| **Giai đoạn 13** | Backend Python FastAPI & Docker | ⏳ Bước tiếp theo | Lưu trữ phiên chơi và chuyển tiếp dữ liệu qua REST API |
| **Giai đoạn 14** | Cloud Monitoring (MQTT + Adafruit IO Dashboard) | ⏳ Chưa thực hiện | Giám sát điểm số, máu, thời gian chơi |

---

## 2. CHI TIẾT 3 CỬ CHỈ CHIẾN ĐẤU CỐT LÕI (COMBAT GESTURES)

1. **⚔️ CHÉM QUÁI (Slash Attack):**
   - **Cơ chế nhận diện:** Phát hiện vận tốc di chuyển tâm bàn tay qua màn hình `handSpeed > 0.70 m/s` (động tác vung tay / chém kiếm khí). Xác nhận cực nhanh (0.05s).
   - **Hiệu ứng chiến đấu:** Raycast và quét góc 60 độ phía trước camera trong cự ly 4.0m, gây 45 sát thương cho quái vật trước mặt. Quái vật bị giật lùi và phát animation `Hit`.
   - **Đồ họa:** Vệt kiếm ánh sáng Neon (Slash Blade FX) lướt chéo qua màn hình cùng biểu ngữ `⚔️ CHÉM TRÚNG! (-45 HP)`.
   - **Phím test PC:** Phím `1`, Phím `SPACE` hoặc Click chuột trái.

2. **🛡️ TƯỜNG CHẮN BẢO VỆ (Shield / OpenPalm):**
   - **Cơ chế nhận diện:** Bàn tay mở thẳng các ngón và giơ cao trước camera (`highHandMinWristY > 0.25`).
   - **Hiệu ứng chiến đấu:** Kích hoạt lá chắn năng lượng bảo vệ trong 2.5s, chặn đứng 100% sát thương từ mọi đòn tấn công của quái vật.
   - **Đồ họa:** Vòm năng lượng màu xanh lam ở mép trái màn hình với chữ `TƯỜNG CHẮN` phát sáng và nút Tường chắn viền vàng rực rỡ.
   - **Phím test PC:** Phím `2`.

3. **✊ LẤY TRÁI TIM / VẬT PHẨM (Grab Item & Collect Heart):**
   - **Cơ chế nhận diện:** Chụm cả bàn tay lại (nắm chặt các ngón tay vào lòng bàn tay hoặc chụm ngón cái - ngón trỏ `Pinch <= 0.10`).
   - **Hiệu ứng chiến đấu:** 
     - Tự động hút Trái Tim Hồi Máu 3D gần nhất (trong cự ly 7.5m) bay vút về phía camera người chơi với gia tốc mượt mà.
     - Hồi ngay `+30 HP` (tối đa 100 HP), hiển thị banner rực rỡ `❤️ +30 HP (HỒI MÁU THÀNH CÔNG!)` và thanh máu chớp sáng xanh lục/vàng.
     - Nếu không có trái tim xung quanh: tự động nhặt vật phẩm rơi rớt cộng thêm 50 điểm thưởng.
   - **Cơ chế sinh tim:** Quái rơi tim khi bị hạ gục (`Die()`), 20% xác suất rơi tim khi bị chém trúng, và tự động spawn tim định kỳ trước mặt người chơi mỗi 12s.
   - **Phím test PC:** Phím `3` hoặc Click vào nút `❤️ Lấy tim/Đồ`.
