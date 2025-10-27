using backend_nhom2.Data;
using backend_nhom2.Domain;
using backend_nhom2.Services.Geo; // <--- thêm
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace backend_nhom2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Owner")]
    public class DiemGiaoController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly GeocodingService _geo;

        public DiemGiaoController(AppDbContext db, GeocodingService geo)
        {
            _db = db;
            _geo = geo;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DiemGiao>>> GetAll()
        {
            var diemGiaos = await _db.DiemGiaos.AsNoTracking().ToListAsync();
            return Ok(diemGiaos);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<DiemGiao>> GetById(string id)
        {
            var diemGiao = await _db.DiemGiaos.FindAsync(id);
            if (diemGiao is null)
                return NotFound($"Không tìm thấy điểm giao với ID: {id}");
            return Ok(diemGiao);
        }

        // === SỬA PHẦN NÀY ===
        [HttpPost]
        public async Task<ActionResult<DiemGiao>> Create([FromBody] DiemGiao diemGiao, CancellationToken ct)
        {
            if (await _db.DiemGiaos.AnyAsync(d => d.IdDD == diemGiao.IdDD))
                return Conflict($"Điểm giao với ID '{diemGiao.IdDD}' đã tồn tại.");

            // Nếu chưa có Lat/Lng → tự geocode từ VITRI
            if ((!diemGiao.Lat.HasValue || !diemGiao.Lng.HasValue) && !string.IsNullOrWhiteSpace(diemGiao.VITRI))
            {
                var geo = await _geo.GeocodeAsync(diemGiao.VITRI, ct);
                if (geo == null)
                    return BadRequest("Không lấy được toạ độ từ địa chỉ. Vui lòng nhập Lat/Lng thủ công hoặc kiểm tra địa chỉ.");

                diemGiao.Lat = geo.Value.lat;
                diemGiao.Lng = geo.Value.lng;
            }

            _db.DiemGiaos.Add(diemGiao);
            await _db.SaveChangesAsync(ct);

            return CreatedAtAction(nameof(GetById), new { id = diemGiao.IdDD }, diemGiao);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, DiemGiao diemGiao)
        {
            if (id != diemGiao.IdDD)
                return BadRequest("ID trong URL và body không khớp.");

            var entity = await _db.DiemGiaos.FindAsync(id);
            if (entity is null)
                return NotFound($"Không tìm thấy điểm giao với ID: {id}");
            // Sửa thì phải sửa thủ công
            entity.TEN = diemGiao.TEN;
            entity.VITRI = diemGiao.VITRI;
            entity.Lat = diemGiao.Lat;
            entity.Lng = diemGiao.Lng;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var diemGiao = await _db.DiemGiaos.FindAsync(id);
            if (diemGiao is null)
                return NotFound($"Không tìm thấy điểm giao với ID: {id}");

            _db.DiemGiaos.Remove(diemGiao);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
