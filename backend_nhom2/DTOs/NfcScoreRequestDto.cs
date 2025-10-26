// File: DTOs/NfcScoreRequestDto.cs

namespace backend_nhom2.DTOs
{
    public class NfcScoreRequestDto
    {
        // ID thẻ NFC được quét (UID của thẻ)
        public string CardId { get; set; } = string.Empty;

        // Số điểm muốn cộng (Owner nhập vào app Flutter)
        public int PointsToAdd { get; set; }
    }
}