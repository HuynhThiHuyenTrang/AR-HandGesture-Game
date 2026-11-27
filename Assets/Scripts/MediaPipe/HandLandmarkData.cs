using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Định danh 21 điểm khớp xương bàn tay theo chuẩn Google MediaPipe Hand Landmarker
/// </summary>
public enum HandJoint
{
    Wrist = 0,              // Cổ tay
    ThumbCMC = 1,           // Khớp gốc ngón cái
    ThumbMCP = 2,           // Khớp đốt 1 ngón cái
    ThumbIP = 3,            // Khớp đốt 2 ngón cái
    ThumbTip = 4,           // Đầu ngón cái

    IndexMCP = 5,           // Khớp gốc ngón trỏ
    IndexPIP = 6,           // Khớp đốt giữa ngón trỏ
    IndexDIP = 7,           // Khớp đốt đầu ngón trỏ
    IndexTip = 8,           // Đầu ngón trỏ

    MiddleMCP = 9,          // Khớp gốc ngón giữa
    MiddlePIP = 10,         // Khớp đốt giữa ngón giữa
    MiddleDIP = 11,         // Khớp đốt đầu ngón giữa
    MiddleTip = 12,         // Đầu ngón giữa

    RingMCP = 13,           // Khớp gốc ngón áp út (nhẫn)
    RingPIP = 14,           // Khớp đốt giữa ngón áp út
    RingDIP = 15,           // Khớp đốt đầu ngón áp út
    RingTip = 16,           // Đầu ngón áp út

    PinkyMCP = 17,          // Khớp gốc ngón út
    PinkyPIP = 18,          // Khớp đốt giữa ngón út
    PinkyDIP = 19,          // Khớp đốt đầu ngón út
    PinkyTip = 20           // Đầu ngón út
}

/// <summary>
/// Các loại cử chỉ bàn tay chuẩn xác theo yêu cầu đề tài AR Monster Hunter:
/// - Slash: Dùng tay vung chém quái vật (Hand Slash)
/// - OpenPalm: Thẳng bàn tay mở -> Bật Tường Chắn (Shield)
/// - Grab: Chụm cả bàn tay lại -> Lấy vật phẩm (Grab Item / Fist)
/// </summary>
public enum HandGestureType
{
    None = 0,               // Bình thường
    Slash = 1,              // CHÉM QUÁI: Vung tay di chuyển nhanh qua màn hình
    OpenPalm = 2,           // TƯỜNG CHẮN: Thẳng bàn tay mở
    Shield = 2,             // Đồng nghĩa OpenPalm (Tường chắn)
    Grab = 3,               // LẤY VẬT PHẨM: Chụm cả bàn tay lại
    Fist = 3,               // Đồng nghĩa Grab (Nắm cả bàn tay lại)
    Push = 4,               // Đẩy lùi
    Release = 5,            // Thả / Ném
    Throw = 5               // Đồng nghĩa Release
}

/// <summary>
/// Cấu trúc lưu trữ dữ liệu 21 điểm mốc (landmarks) của một bàn tay
/// </summary>
[Serializable]
public class HandLandmarkResult
{
    [Tooltip("Bàn tay trái hay bàn tay phải")]
    public bool isRightHand = true;

    [Tooltip("Độ tin cậy của thuật toán phát hiện (0.0 đến 1.0)")]
    public float confidence = 0f;

    [Tooltip("Tọa độ 3D chuẩn hóa (X, Y từ 0 đến 1, Z là chiều sâu) của 21 điểm mốc")]
    public Vector3[] landmarks = new Vector3[21];

    [Tooltip("Kích thước bàn tay chuẩn hóa (Rộng x Cao)")]
    public float handWidth = 0f;
    public float handHeight = 0f;
    [Tooltip("Mật độ lấp đầy pixel da trong khung bao (cao = chụm/nắm tay, thấp = xòe ngón)")]
    public float fillRatio = 0f;
    [Tooltip("Số đỉnh ngón tay tách biệt ở mép trên bàn tay (xòe tay >= 2, mặt/nắm tay <= 1)")]
    public int fingerPeaks = 0;
    [Tooltip("Bàn tay có chuyển động gần đây (phân biệt tay giơ lên với khuôn mặt đứng yên)")]
    public bool isMoving = false;
    public Vector3 centroid = Vector3.zero;

    public HandLandmarkResult()
    {
        for (int i = 0; i < 21; i++)
        {
            landmarks[i] = Vector3.zero;
        }
    }

    /// <summary>
    /// Lấy tọa độ một khớp bất kỳ
    /// </summary>
    public Vector3 GetJoint(HandJoint joint)
    {
        return landmarks[(int)joint];
    }

    /// <summary>
    /// Khoảng cách chuẩn hóa giữa đầu ngón cái (ThumbTip) và đầu ngón trỏ (IndexTip)
    /// Dùng cho nhận diện cử chỉ GRAB (Pinch) và RELEASE
    /// </summary>
    public float GetPinchDistance()
    {
        return Vector3.Distance(GetJoint(HandJoint.ThumbTip), GetJoint(HandJoint.IndexTip));
    }
}
