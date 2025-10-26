// File: Controllers/OwnerController.cs

using backend_nhom2.Data;
using backend_nhom2.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace backend_nhom2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Owner")] // Chỉ Owner được vào
    public class OwnerController : ControllerBase
    {
        private readonly AppDbContext _context; // Biến DbContext

        // Constructor để inject DbContext
        public OwnerController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("dashboard")]
        public IActionResult GetOwnerDashboard()
        {
            var ownerUsername = User.Identity?.Name ?? "Không xác định";
            return Ok($"Chào mừng chủ xe {ownerUsername}. Đây là dữ liệu dashboard của bạn.");
        }

        [HttpPost("add-vehicle")]
        public IActionResult AddVehicle([FromBody] object vehicleData)
        {
            // Logic để thêm xe mới
            return Ok("Đã thêm xe mới thành công! (Chỉ Owner mới làm được)");
        }

        // THÊM MỚI: API CỘNG ĐIỂM BẰNG NFC
        /// <summary>
        /// Owner quét thẻ NFC để cộng điểm cho Driver liên quan.
        /// </summary>
        [HttpPost("add-score")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> AddScore([FromBody] NfcScoreRequestDto request)
        {
            // Kiểm tra tính hợp lệ của số điểm
            if (request.PointsToAdd <= 0)
            {
                return BadRequest("Số điểm cộng phải lớn hơn 0.");
            }

            // 1. Tìm Driver (User) dựa trên NfcCardId và đảm bảo là vai trò Driver
            var driver = await _context.Users
                .Include(u => u.Role)
                .SingleOrDefaultAsync(u =>
                    u.NfcCardId == request.CardId &&
                    u.Role.RoleName == "Driver");

            if (driver == null)
            {
                return NotFound($"Không tìm thấy tài xế với ID thẻ NFC: {request.CardId}.");
            }

            // 2. Thực hiện cộng điểm
            driver.Score += request.PointsToAdd;

            // 3. Lưu thay đổi vào Database
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Đã cộng {request.PointsToAdd} điểm cho tài xế {driver.FullName}.",
                newScore = driver.Score
            });
        }
    }
}