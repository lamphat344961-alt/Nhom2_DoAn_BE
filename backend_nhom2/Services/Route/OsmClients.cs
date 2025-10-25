using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace backend_nhom2.Services.Route
{
    public class OsmClients
    {
        private readonly HttpClient _http;

        public class OsrmResponse
        {
            public string code { get; set; }
            public OsrmRoute[] routes { get; set; }
        }
        public class OsrmRoute
        {
            public double duration { get; set; } // seconds
            public string geometry { get; set; } // encoded polyline
        }

        public class RouteLegResult
        {
            public int DurationSec { get; set; }
            public string Polyline { get; set; }
        }

        public OsmClients(HttpClient http)
        {
            _http = http;
            _http.Timeout = TimeSpan.FromSeconds(10);
            // OSRM public server (no key). You can swap to your own instance later.
            _http.BaseAddress = new Uri("https://router.project-osrm.org/");
        }

        /// <summary>
        /// Lấy đường bộ + encoded polyline giữa 2 điểm bằng OSRM.
        /// </summary>
        public async Task<RouteLegResult?> RouteAsync(double lat1, double lng1, double lat2, double lng2)
        {
            // OSRM nhận (lng,lat)
            var url = $"route/v1/driving/{lng1.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lat1.ToString(System.Globalization.CultureInfo.InvariantCulture)};" +
                      $"{lng2.ToString(System.Globalization.CultureInfo.InvariantCulture)},{lat2.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                      "?overview=full&geometries=polyline";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadFromJsonAsync<OsrmResponse>(new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (json == null || json.code != "Ok" || json.routes == null || json.routes.Length == 0)
                return null;

            var r = json.routes[0];
            return new RouteLegResult
            {
                DurationSec = (int)Math.Round(r.duration),
                Polyline = r.geometry ?? ""
            };
        }
    }
}
