using backend_nhom2.Data;
using backend_nhom2.DTOs;
using backend_nhom2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace backend_nhom2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Owner")] // Chỉ Owner được truy cập controller này
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;

        public UserController(AppDbContext context)
        {
            _context = context;
        }

        // Dùng để lấy về tất cả người dùng có vai trò là "Driver"
        // GET: api/User/drivers
        [HttpGet("drivers")]
        public async Task<IActionResult> GetDrivers()
        {
            var drivers = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.Role.RoleName == "Driver")
                .Select(u => new
                {
                    u.UserId,
                    u.Username,
                    u.FullName,
                    u.PhoneNumber,
                    u.CCCD
                })
                .AsNoTracking()
                .ToListAsync();

            return Ok(drivers);
        }

        // Owner tạo tài khoản cho tài xế mới
        // POST: api/User/create-driver
        [HttpPost("create-driver")]
        public async Task<IActionResult> CreateDriver(RegisterRequestDto request)
        {
            if (request.Role.Trim().ToLower() != "driver")
            {
                return BadRequest("Chỉ có thể tạo tài khoản với vai trò 'Driver'.");
            }

            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest("Username đã tồn tại.");
            }

            var driverRole = await _context.Roles.SingleOrDefaultAsync(r => r.RoleName == "Driver");
            if (driverRole == null)
            {
                return StatusCode(500, "Không tìm thấy vai trò 'Driver' trong hệ thống.");
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User
            {
                Username = request.Username,
                PasswordHash = passwordHash,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                CCCD = request.CCCD,
                RoleId = driverRole.RoleId,
                CreatedAt = System.DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Tạo tài khoản cho tài xế {user.FullName} thành công!" });
        }
    }
}