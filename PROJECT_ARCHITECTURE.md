# KIẾN TRÚC TỔNG THỂ HỆ THỐNG (PROJECT ARCHITECTURE)
**Dự án:** Ứng dụng Game Thực Tế Tăng Cường (AR) Điều Khiển Bằng Cử Chỉ Bàn Tay Với MediaPipe, Unity, Mobile & Cloud Monitoring  
**Môi trường:** Unity 6 (Frontend) + Python FastAPI (Backend) + Adafruit IO (Cloud Dashboard)

---

## 1. SƠ ĐỒ KIẾN TRÚC 3 TẦNG (THREE-TIER ARCHITECTURE)

```
+-----------------------------------------------------------------------------------+
|                           TẦNG 1: FRONTEND GAME (UNITY)                           |
|                                                                                   |
|  [AR Foundation / ARCore / XR Simulation]                                          |
|      - Camera Feed                                                                |
|      - Plane Detection (Sàn, bàn, tường)                                          |
|      - Raycast & Surface Tracking                                                 |
|                                                                                   |
|  [MediaPipe Hand Landmarker Module]                                               |
|      - Lấy Frame từ AR Camera (Tránh xung đột tài nguyên camera)                   |
|      - Trích xuất 21 Hand Landmarks cục bộ trên thiết bị                          |
|                                                                                   |
|  [Gesture Recognition & Controller]                                               |
|      - Làm mượt Landmark (Smoothing Filter)                                       |
|      - Nhận diện cử chỉ: Pinch (Nắm), Throw (Ném), Shield (Khiên), Push (Đẩy)      |
|      - Kiểm tra độ tin cậy (Confidence & Stability)                               |
|      - Cooldown & Tránh kích hoạt đúp                                             |
|                                                                                   |
|  [Core Gameplay & Combat Engine]                                                  |
|      - Monster AI (FSM: Idle, Detect, Chase, Telegraph, Attack, Hit, Die)          |
|      - Monster Spawner & Level System (5 Cấp độ, điều chỉnh theo Inspector)      |
|      - Player Health & Score Manager                                              |
|      - PC Input Fallback (Chuột & Bàn phím phục vụ kiểm thử)                      |
|                                                                                   |
|  [Game Network Client]                                                            |
|      - Gửi HTTP REST Event (Game Start, Level Up, Monster Killed, Game Over)      |
+-----------------------------------------------------------------------------------+
                                       |
                                       | HTTP REST API (JSON Event & Stats)
                                       v
+-----------------------------------------------------------------------------------+
|                        TẦNG 2: BACKEND SERVER (PYTHON FASTAPI)                    |
|                                                                                   |
|  [API Router & Validation]                                                        |
|      - /api/v1/session/start                                                      |
|      - /api/v1/session/event                                                      |
|      - /api/v1/session/end                                                        |
|      - /api/v1/stats                                                              |
|                                                                                   |
|  [Session Manager & Storage]                                                      |
|      - Quản lý phiên chơi người dùng                                              |
|      - Thống kê: Score, Kill Count, Cử chỉ dùng, Thời lượng chơi, Latency         |
|      - Dockerized Container                                                       |
|                                                                                   |
|  [Cloud Bridge Client]                                                            |
|      - Lưu trữ bảo mật Adafruit IO Key & Username                                 |
|      - Xuất bản dữ liệu qua MQTT / REST Client                                   |
+-----------------------------------------------------------------------------------+
                                       |
                                       | MQTT / REST (Bảo mật API Key tại Server)
                                       v
+-----------------------------------------------------------------------------------+
|                     TẦNG 3: CLOUD MONITORING (ADAFRUIT IO)                        |
|                                                                                   |
|  [Adafruit IO Feeds]                                                              |
|      - feed/player-score                                                          |
|      - feed/player-health                                                         |
|      - feed/monster-kills                                                         |
|      - feed/current-level                                                         |
|      - feed/game-status                                                           |
|      - feed/gesture-stats                                                         |
|                                                                                   |
|  [Realtime Visual Dashboard]                                                      |
|      - Biểu đồ điểm số thời gian thực                                             |
|      - Gauge đo máu người chơi                                                    |
|      - Bảng theo dõi số lượng quái bị hạ                                          |
|      - Trạng thái phiên chơi (Playing / Game Over / Victory)                      |
+-----------------------------------------------------------------------------------+
```

---

## 2. NGUYÊN TẮC LUỒNG DỮ LIỆU & BẢO MẬT

1. **Xử lý thị giác hoàn toàn cục bộ (Edge Computing):**
   - Không gửi dữ liệu hình ảnh camera hay tọa độ 21 điểm landmarks lên Cloud.
   - Việc nhận diện cử chỉ được xử lý 100% trong máy người chơi nhằm đảm bảo tốc độ phản hồi (low latency) và bảo mật quyền riêng tư của camera.

2. **Bảo mật Cloud Key:**
   - Client Unity **tuyệt đối không nhúng Adafruit IO AIO-Key**.
   - Client chỉ gửi API có xác thực cơ bản về Backend FastAPI cục bộ hoặc Server nội bộ.
   - Backend FastAPI chịu trách nhiệm quản lý biến môi trường (`.env`), lưu AIO-Key và đẩy số liệu lên Adafruit IO.

3. **Cơ chế Camera không xung đột trên Android:**
   - AR Foundation (ARCore) giữ quyền truy cập phần cứng Camera.
   - Module MediaPipe sẽ nhận frame hình ảnh từ `ARCameraManager.TryAcquireLatestCpuImage` để xử lý landmark thay vì mở một camera session độc lập.

---

## 3. THIẾT KẾ CÂN BẰNG THỜI GIAN VÀ ĐỘ TRỄ (TIMING & LATENCY)

Hệ thống phân biệt rõ ràng giữa các thông số thời gian:

```
[Spawn Quái] 
    |
    v (Sau Spawn Interval: 5s -> 1s tùy level)
[Quái Xuất Hiện] ---> [Idle / Detect] ---> [Chase tiếp cận người chơi]
                                                |
                                                v
                                         [Telegraph: 1.5s] (Báo hiệu chuẩn bị đánh)
                                                |
           +------------------------------------+------------------------------------+
           |                                                                         |
(Người chơi không kịp phản ứng)                                     (Người chơi làm cử chỉ tay)
           |                                                                         |
           v                                                                         v
   [Monster Attack]                                                      [Camera Frame Capture]
           |                                                                         |
           v                                                                         v
   [Player Nhận Damage]                                                  [MediaPipe Detect Landmark]
           |                                                                         |
           v                                                                         v
   [Attack Cooldown: 3s]                                                 [Confirm Gesture: 0.3s]
                                                                                     |
                                                                                     v
                                                                         [Thực Hiện Đòn Đánh/Khiên]
                                                                                     |
                                                                                     v
                                                                         [Quái Nhận Hit / Bị Chặn]
                                                                                     |
                                                                                     v
                                                                         [Quái Hết HP -> Die Animation]
                                                                                     |
                                                                                     v
                                                                         [Despawn Delay: 2s -> Xóa Object]
```

- **Spawn Interval:** Khoảng cách giữa 2 lần xuất hiện quái vật (Level 1: 5s, Level 2: 4s, ..., Level 5: 1s).
- **Player Reaction Window:** Khoảng thời gian người chơi có cơ hội phản ứng trước khi bị trúng đòn (6s - 8s).
- **Telegraph Duration:** Thời gian quái báo hiệu động tác sắp đánh (1.5s).
- **Gesture Confirmation Duration:** Thời gian xác nhận cử chỉ ổn định (0.3s).
- **Gesture Cooldown:** Thời gian chờ giữa 2 lần nhận diện cùng 1 cử chỉ để chống spam (0.5s).
- **Despawn Delay:** Thời gian chờ sau khi chạy xong animation chết trước khi hủy GameObject (2s).

---

## 4. QUẢN LÝ TRẠNG THÁI AI QUÁI VẬT (FINITE STATE MACHINE - FSM)

Mỗi quái vật hoạt động theo một máy trạng thái hữu hạn rõ ràng:

1. **State Spawn:** Khởi tạo vị trí, phát hiệu ứng xuất hiện.
2. **State Idle:** Quan sát, định vị người chơi trong không gian AR.
3. **State Chase (Cận chiến):** Di chuyển về phía người chơi cho đến khi vào tầm đánh.
4. **State KeepDistance (Tầm xa):** Lùi lại hoặc giữ cự ly an toàn để bắn đạn.
5. **State Telegraph:** Báo hiệu sắp tung đòn (gầm, giơ vuốt, tụ năng lượng).
6. **State Attack:** Tung đòn cận chiến hoặc bắn đạn (Projectile).
7. **State Cooldown:** Chờ hồi chiêu trước khi bắt đầu chu kỳ tấn công mới.
8. **State Hit (Damage):** Bị gián đoạn, phát animation trúng đòn hoặc hiệu ứng choáng.
9. **State Die:** HP = 0, dừng mọi hành vi tấn công, phát animation chết, thông báo cộng điểm và xóa quái sau Despawn Delay.
