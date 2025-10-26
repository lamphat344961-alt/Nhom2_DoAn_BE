using backend_nhom2.Domain;

namespace backend_nhom2.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string? PhoneNumber { get; set; }
        public string? CCCD { get; set; }
        public DateTime CreatedAt { get; set; }
        public int RoleId { get; set; }
        public Role Role { get; set; } = null!;

        // Nếu user này là tài xế, đây là xe họ được gán
        public Xe? Xe { get; set; }


        // === THÊM MỚI CHO CHỨC NĂNG NFC/ĐIỂM THƯỞNG ===
        public string? NfcCardId { get; set; } // ID thẻ NFC duy nhất
        public int Score { get; set; } = 0;    // Điểm thưởng của Driver
        // ===============================================
    }
}