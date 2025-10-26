using Microsoft.AspNetCore.Mvc;
using backend_nhom2.Data;
using backend_nhom2.DTOs;
using backend_nhom2.Models;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System;

namespace backend_nhom2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // Chức năng này chỉ nên được thực hiện một lần để khởi tạo hệ thống.
        [HttpPost("register-first-owner")]
        public async Task<IActionResult> RegisterFirstOwner(RegisterRequestDto request)
        {
            var ownerRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Owner");
            if (ownerRole == null)
            {
                return StatusCode(500, "Vai trò 'Owner' chưa tồn tại trong CSDL.");
            }

            bool ownerExists = await _context.Users.AnyAsync(u => u.RoleId == ownerRole.RoleId);
            if (ownerExists)
            {
                return BadRequest("Hệ thống đã có Owner. Không thể tạo thêm bằng chức năng này.");
            }

            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
            {
                return BadRequest("Username đã tồn tại.");
            }

            // Gọi đầy đủ BCrypt.Net.BCrypt
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User
            {
                Username = request.Username,
                PasswordHash = passwordHash,
                FullName = request.FullName,
                PhoneNumber = request.PhoneNumber,
                CCCD = request.CCCD,
                RoleId = ownerRole.RoleId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tạo tài khoản Owner đầu tiên thành công!" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequestDto request)
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            // Gọi đầy đủ BCrypt.Net.BCrypt
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Tên đăng nhập hoặc mật khẩu không đúng.");
            }

            // Thêm toán tử '!' để tắt cảnh báo null reference
            var token = GenerateJwtToken(user!);

            return Ok(new LoginResponseDto
            {
                Token = token,
                FullName = user.FullName,
                Role = user.Role.RoleName
            });
        }

        private string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"];
            if (string.IsNullOrEmpty(jwtKey))
            {
                throw new InvalidOperationException("JWT Key chưa được cấu hình trong appsettings.json");
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Role, user.Role.RoleName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddDays(1),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}