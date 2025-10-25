using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using backend_nhom2.DTOs.Route;
using backend_nhom2.Models;
using backend_nhom2.Services.Route;
using backend_nhom2.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend_nhom2.Models.Route;
using backend_nhom2.Data;

namespace backend_nhom2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RouteController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly OsmClients _osm;

        public RouteController(AppDbContext context, OsmClients osm)
        {
            _context = context;
            _osm = osm;
        }

        // ===== Helpers =====
        private static bool IsMillis(long v) => v > 10_000_000_000L;

        private static TimeZoneInfo GetVnTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
            catch { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        }

        private static long TodayMidnightEpochUtcFromDeparture(long departureEpochUtc, TimeZoneInfo tz)
        {
            var depUtc = DateTimeOffset.FromUnixTimeSeconds(departureEpochUtc).UtcDateTime;
            var depLocal = TimeZoneInfo.ConvertTimeFromUtc(depUtc, tz);
            var localMidnight = new DateTime(depLocal.Year, depLocal.Month, depLocal.Day, 0, 0, 0, DateTimeKind.Unspecified);
            var backUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz);
            return new DateTimeOffset(backUtc).ToUnixTimeSeconds();
        }

        private static (long? absStart, long? absEnd) NormalizeWindowsToAbsolute(
            long departureEpochUtc, long? windowStartRaw, long? windowEndRaw, TimeZoneInfo tz)
        {
            if (windowStartRaw is null || windowEndRaw is null) return (null, null);

            long ws = windowStartRaw.Value;
            long we = windowEndRaw.Value;

            if (IsMillis(ws)) ws /= 1000;
            if (IsMillis(we)) we /= 1000;

            // User nhập "giờ trong ngày" (0..86399) -> ghép với "hôm nay" theo TZ
            if (0 <= ws && ws < 86400 && 0 <= we && we < 86400)
            {
                var midnight = TodayMidnightEpochUtcFromDeparture(departureEpochUtc, tz);
                ws = midnight + ws;
                we = midnight + we;
            }

            if (we < ws) we = ws;
            return (ws, we);
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180.0) *
                       Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        // ====================================================
        //  POST /api/Route/optimize
        // ====================================================
        [HttpPost("optimize")]
        public async Task<IActionResult> Optimize([FromBody] OptimizeRequest req)
        {
            if (req is null || req.points is null || req.points.Count < 2)
                return BadRequest("Cần ít nhất 2 điểm (bao gồm depot).");

            long nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long departureEpoch = (req.departureEpoch > 0) ? req.departureEpoch : nowEpoch;

            var tz = GetVnTimeZone();
            var points = new List<OptimizePoint>(req.points.Count);
            foreach (var p in req.points)
            {
                long? wsAbs = null, weAbs = null;
                if (p.windowStart.HasValue && p.windowEnd.HasValue)
                {
                    var (ws, we) = NormalizeWindowsToAbsolute(departureEpoch, p.windowStart, p.windowEnd, tz);
                    wsAbs = ws; weAbs = we;
                }

                points.Add(new OptimizePoint(
                    id: p.id,
                    name: p.name,
                    lat: p.lat,
                    lng: p.lng,
                    windowStart: wsAbs,
                    windowEnd: weAbs,
                    serviceMinutes: p.serviceMinutes
                ));
            }

            return await RunOptimizationAndPersistAsync(points, (int)departureEpoch, Math.Max(5, req.vehicleSpeedKph));
        }

        // ====================================================
        //  POST /api/Route/optimize-from-orders
        // ====================================================
        [HttpPost("optimize-from-orders")]
        [Authorize(Roles = "Owner,Admin")]
        public async Task<IActionResult> OptimizeFromOrders([FromBody] OptimizeFromOrdersRequest req)
        {
            if (req is null || req.orderIds is null || req.orderIds.Count == 0)
                return BadRequest("Danh sách đơn hàng trống.");

            var orders = await _context.DonHangs
                .Include(d => d.DiemGiao)
                .Where(d => req.orderIds.Contains(d.MADON))
                .ToListAsync();

            if (!orders.Any()) return NotFound("Không tìm thấy đơn hàng hợp lệ.");

            long nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long departureEpoch = (req.departureEpoch > 0) ? req.departureEpoch : nowEpoch;
            var tz = GetVnTimeZone();

            var points = new List<OptimizePoint>();

            // Depot (id 0) — có thể thay toạ độ từ cấu hình
            points.Add(new OptimizePoint(
                id: 0, name: "Depot",
                lat: 10.77, lng: 106.70,
                windowStart: null, windowEnd: null,
                serviceMinutes: 0
            ));

            int nextId = 1;
            foreach (var d in orders)
            {
                long? wsAbs = d.WindowStart;
                long? weAbs = d.WindowEnd;
                if (wsAbs.HasValue && weAbs.HasValue)
                {
                    var (ws, we) = NormalizeWindowsToAbsolute(departureEpoch, wsAbs, weAbs, tz);
                    wsAbs = ws; weAbs = we;
                }

                points.Add(new OptimizePoint(
                    id: nextId++,
                    name: d.DiemGiao?.TEN ?? $"DG-{d.MADON}",
                    lat: d.DiemGiao?.Lat ?? 0,
                    lng: d.DiemGiao?.Lng ?? 0,
                    windowStart: wsAbs,
                    windowEnd: weAbs,
                    serviceMinutes: d.ServiceMinutes ?? 10
                ));
            }

            return await RunOptimizationAndPersistAsync(points, (int)departureEpoch, Math.Max(5, req.vehicleSpeedKph));
        }

        // ====================================================
        //  Private: chạy tối ưu & lưu (ĐƯỜNG BỘ + POLYLINE)
        // ====================================================
        private async Task<IActionResult> RunOptimizationAndPersistAsync(List<OptimizePoint> points, int departureEpoch, int vehicleSpeedKph)
        {
            int n = points.Count;

            // Ma trận thời gian sơ bộ để solver chạy (fallback Haversine)
            var travelTimeSec = new int[n, n];
            double speed = vehicleSpeedKph;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j) { travelTimeSec[i, j] = 0; continue; }
                    double km = HaversineKm(points[i].lat, points[i].lng, points[j].lat, points[j].lng);
                    int sec = (int)Math.Round(km / speed * 3600.0);
                    travelTimeSec[i, j] = sec < 0 ? 0 : sec;
                }
            }

            var tws = points.Skip(1).Select(p => new Optimizer.TwNode
            {
                Start = p.windowStart ?? (long.MinValue / 2),   
                End = p.windowEnd ?? (long.MaxValue / 2),   
                ServiceSeconds = (p.serviceMinutes > 0 ? p.serviceMinutes : 10) * 60
            }).ToArray();

            var (order, totalSecByCheapestArc) = Optimizer.SolveSingleVehicleTW(travelTimeSec, tws, 0, departureEpoch);
            if (order.Length <= 1) return StatusCode(500, "Không tìm thấy route hợp lệ.");

            // Xây dựng stops + tính ETA bằng THỜI GIAN ĐƯỜNG BỘ từ OSRM
            var stops = new List<OptimizedStop>();
            long cumSec = 0;
            int totalRealSec = 0;

            for (int k = 0; k < order.Length; k++)
            {
                int idx = order[k];
                var pt = points[idx];

                long etaEpoch = departureEpoch + cumSec;
                string etaIso = DateTimeOffset.FromUnixTimeSeconds(etaEpoch).ToString("o");

                stops.Add(new OptimizedStop(
                    pointId: pt.id,
                    name: pt.name,
                    lat: pt.lat,
                    lng: pt.lng,
                    etaEpoch: etaEpoch,
                    etaIso: etaIso,
                    polyline: string.Empty // gán sau cho các leg k..k+1
                ));

                if (k < order.Length - 1)
                {
                    int from = order[k];
                    int to = order[k + 1];
                    var a = points[from];
                    var b = points[to];

                    // GỌI OSRM – đường bộ + polyline
                    var leg = await _osm.RouteAsync(a.lat, a.lng, b.lat, b.lng);

                    int legDur = 0;
                    string legPolyline = "";
                    if (leg != null && leg.DurationSec > 0)
                    {
                        legDur = leg.DurationSec;
                        legPolyline = leg.Polyline ?? "";
                    }
                    else
                    {
                        // fallback: Haversine nếu OSRM lỗi
                        legDur = travelTimeSec[from, to];
                        legPolyline = "";
                    }

                    // gán polyline cho stop hiện tại (từ stop k sang k+1)
                    stops[k] = new OptimizedStop(
                        pointId: stops[k].pointId,
                        name: stops[k].name,
                        lat: stops[k].lat,
                        lng: stops[k].lng,
                        etaEpoch: stops[k].etaEpoch,
                        etaIso: stops[k].etaIso,
                        polyline: legPolyline
                    );

                    // cộng thời gian leg + service tại điểm from (nếu không phải depot)
                    int svc = (from == 0) ? 0 : Math.Max(0, points[from].serviceMinutes) * 60;
                    cumSec += Math.Max(0, legDur) + svc;
                    totalRealSec += Math.Max(0, legDur) + svc;
                }
            }

            // Lưu DB
            var route = new RoutePlan
            {
                CreatedAt = DateTime.UtcNow,
                TotalSeconds = totalRealSec,
                Note = $"Auto route {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
                Stops = stops.Select((s, i) => new RouteStop
                {
                    Order = i,
                    Name = s.name,
                    Lat = s.lat,
                    Lng = s.lng,
                    EtaEpoch = s.etaEpoch,
                    EtaIso = s.etaIso,
                    Polyline = s.polyline
                }).ToList()
            };

            _context.RoutePlans.Add(route);
            await _context.SaveChangesAsync();

            var result = new OptimizeResult(
                routeId: route.Id,
                stops: stops,
                totalSeconds: totalRealSec,
                readableTotal: TimeSpan.FromSeconds(totalRealSec).ToString(@"hh\:mm\:ss")
            );

            return Ok(result);
        }
    }
}
