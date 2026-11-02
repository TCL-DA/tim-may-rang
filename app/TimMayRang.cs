// OTL Tìm máy rang — dò máy rang trong mạng LAN, mở màn hình máy trên trình duyệt, tạo icon ngoài desktop.
//
// App tự dựng một máy chủ web nhỏ chỉ nghe 127.0.0.1, mở trang index.html (nhúng trong exe) bằng trình
// duyệt mặc định. Trang gọi /api/tim để dò; app làm phần trình duyệt bị cấm: quét cổng trong LAN,
// mở trang có chứng chỉ tự ký, ghi file ra desktop.
//
// Viết theo C# 5 để csc có sẵn trong .NET Framework 4.x biên dịch được — máy khách không cần cài gì.
// Build: app\build.ps1

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

[assembly: AssemblyTitle("OTL Tìm máy rang")]
[assembly: AssemblyProduct("OTL Tìm máy rang")]
[assembly: AssemblyCompany("OTL Roaster")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class TimMayRang
{
    const string Ban = "1.0";
    const int CongDau = 47830;          // cổng localhost; bận thì thử 9 cổng kế tiếp
    const int ChoNoiMs = 700;           // chờ một cổng TCP trả lời
    // Trang web client WinCC Unified: panel Unified Comfort để ở /device/WebRH (/WebRH của panel là 503
    // của Industrial Edge, đo trên MTP1200 02/11/2026); Unified PC Runtime để ở /WebRH.
    static readonly string[] DuongManHinh = { "/device/WebRH", "/WebRH" };

    static int cong;
    static DateTime lanCuoiTrang = DateTime.Now;
    static bool trangDaMo;
    static readonly object khoaLog = new object();
    static string thuMucApp;
    static string thuMucThu;            // khác null khi chạy --thu

    // Máy tìm được ở lượt dò gần nhất — /api/mo chỉ mở địa chỉ nằm trong đây hoặc IP khách tự nhập.
    static readonly Dictionary<string, May> mayDaThay = new Dictionary<string, May>();

    class May
    {
        public string Ip;
        public string Url;
        public string TrangThai;   // "san-sang" | "chua-bat-web"
        public int MaHttp;
    }

    class Dai
    {
        public string Ten;         // "Wi-Fi", "mạng dây"…
        public string Mang;        // 192.168.1.0/24
        public List<IPAddress> Host = new List<IPAddress>();
    }

    [STAThread]
    static int Main(string[] args)
    {
        thuMucApp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OTL", "TimMayRang");
        Directory.CreateDirectory(thuMucApp);
        Log("Khởi động bản " + Ban);

        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; }; // màn hình máy dùng chứng chỉ tự ký
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; }   // TLS 1.3, có từ .NET 4.8 trên Windows 11
        catch (NotSupportedException) { }
        ServicePointManager.DefaultConnectionLimit = 64;

        if (args.Length >= 2 && args[0] == "--do")   // dò không cần trình duyệt, in kết quả ra nhật ký: --do 192.168.0.0/24
        {
            DoMang(new[] { DaiTuChuoi(args[1]) }, null).Wait();
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--thu")  // bài thử: không mở trình duyệt, icon ghi vào thư mục cho trước, dò cả máy này
            thuMucThu = args[1];

        TcpListener nghe = null;
        for (int c = CongDau; c < CongDau + 10 && nghe == null; c++)
        {
            try { nghe = new TcpListener(IPAddress.Loopback, c); nghe.Start(); cong = c; }
            catch (SocketException)
            {
                nghe = null;
                if (LaAppDangChay(c)) { MoTrinhDuyet("http://127.0.0.1:" + c + "/"); return 0; }
            }
        }
        if (nghe == null) { Log("Không mở được cổng localhost nào"); return 1; }
        Log("Nghe ở 127.0.0.1:" + cong);

        new Thread(TuTat) { IsBackground = true }.Start();
        MoTrinhDuyet("http://127.0.0.1:" + cong + "/");

        while (true)
        {
            TcpClient k = nghe.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem(delegate { PhucVu(k); });
        }
    }

    // Trang gửi nhịp 5 giây một lần. Đóng trang 20 giây thì app tự tắt; không ai mở trang thì tắt sau 5 phút.
    static void TuTat()
    {
        while (true)
        {
            Thread.Sleep(2000);
            double giay = (DateTime.Now - lanCuoiTrang).TotalSeconds;
            if ((trangDaMo && giay > 20) || (!trangDaMo && giay > 300)) { Log("Tự tắt"); Environment.Exit(0); }
        }
    }

    // ── Máy chủ web nhỏ ──────────────────────────────────────────────────────────────────────────

    static void PhucVu(TcpClient k)
    {
        try
        {
            using (k)
            using (NetworkStream s = k.GetStream())
            {
                string dau = DocDau(s);
                if (dau == null) return;
                string[] dong = dau.Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] yc = dong[0].Split(' ');
                if (yc.Length < 2) return;

                // Chặn DNS rebinding: chỉ phục vụ khi trình duyệt gọi đúng 127.0.0.1/localhost.
                string host = dong.Skip(1).Where(d => d.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.Substring(5).Trim().ToLowerInvariant()).FirstOrDefault() ?? "";
                if (host != "127.0.0.1:" + cong && host != "localhost:" + cong) { GuiChu(s, 403, "text/plain", "Forbidden"); return; }

                string duong = yc[1], hoi = "";
                int q = duong.IndexOf('?');
                if (q >= 0) { hoi = duong.Substring(q + 1); duong = duong.Substring(0, q); }
                Dictionary<string, string> thamSo = TachHoi(hoi);

                lanCuoiTrang = DateTime.Now;
                switch (duong)
                {
                    case "/api/ten": GuiChu(s, 200, "application/json", "{\"app\":\"otl-tim-may-rang\",\"ban\":\"" + Ban + "\"}"); return;
                    case "/api/song": trangDaMo = true; GuiChu(s, 200, "application/json", "{}"); return;
                    case "/api/tim": Tim(s); return;
                    case "/api/mo": Mo(s, thamSo); return;
                    case "/api/thu": Thu(s, thamSo); return;
                }
                if (duong == "/") duong = "/index.html";
                byte[] tep = TaiNguyen(duong.TrimStart('/'));
                if (tep == null) { GuiChu(s, 404, "text/plain", "Not found"); return; }
                GuiByte(s, 200, KieuTep(duong), tep);
            }
        }
        catch (Exception e) { Log("Lỗi phục vụ: " + e.Message); }
    }

    static string DocDau(NetworkStream s)
    {
        var buf = new List<byte>();
        int b;
        while ((b = s.ReadByte()) >= 0)
        {
            buf.Add((byte)b);
            int n = buf.Count;
            if (n >= 4 && buf[n - 4] == 13 && buf[n - 3] == 10 && buf[n - 2] == 13 && buf[n - 1] == 10)
                return Encoding.ASCII.GetString(buf.ToArray());
            if (n > 16384) return null;
        }
        return null;
    }

    static Dictionary<string, string> TachHoi(string hoi)
    {
        var d = new Dictionary<string, string>();
        foreach (string p in hoi.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int i = p.IndexOf('=');
            if (i > 0) d[Uri.UnescapeDataString(p.Substring(0, i))] = Uri.UnescapeDataString(p.Substring(i + 1).Replace('+', ' '));
        }
        return d;
    }

    static void GuiChu(Stream s, int ma, string kieu, string noiDung)
    {
        GuiByte(s, ma, kieu + "; charset=utf-8", Encoding.UTF8.GetBytes(noiDung));
    }

    static void GuiByte(Stream s, int ma, string kieu, byte[] noiDung)
    {
        string dau = "HTTP/1.1 " + ma + " " + (ma == 200 ? "OK" : "Error") + "\r\nContent-Type: " + kieu +
            "\r\nContent-Length: " + noiDung.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        byte[] d = Encoding.ASCII.GetBytes(dau);
        s.Write(d, 0, d.Length);
        s.Write(noiDung, 0, noiDung.Length);
    }

    static byte[] TaiNguyen(string ten)
    {
        using (Stream r = Assembly.GetExecutingAssembly().GetManifestResourceStream(ten))
        {
            if (r == null) return null;
            var m = new MemoryStream();
            r.CopyTo(m);
            return m.ToArray();
        }
    }

    static string KieuTep(string duong)
    {
        switch (Path.GetExtension(duong).ToLowerInvariant())
        {
            case ".html": return "text/html; charset=utf-8";
            case ".svg": return "image/svg+xml";
            case ".jpg": return "image/jpeg";
            case ".png": return "image/png";
            case ".ico": return "image/x-icon";
            default: return "application/octet-stream";
        }
    }

    // ── /api/tim: dò cả mạng, báo tiến độ bằng Server-Sent Events ──────────────────────────────────

    static void Tim(NetworkStream s)
    {
        byte[] dau = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        s.Write(dau, 0, dau.Length);
        object khoa = new object();
        Action<string, string> gui = delegate (string loai, string json)
        {
            byte[] d = Encoding.UTF8.GetBytes("event: " + loai + "\ndata: " + json + "\n\n");
            lock (khoa) { try { s.Write(d, 0, d.Length); s.Flush(); } catch (IOException) { } }
        };

        List<Dai> dai = LayDaiMang();
        Log("Dò " + dai.Count + " dải: " + string.Join(", ", dai.Select(x => x.Ten + " " + x.Mang)));
        gui("mang", "{\"dai\":[" + string.Join(",", dai.Select(x => "{\"ten\":" + Js(x.Ten) + ",\"mang\":" + Js(x.Mang) + "}")) + "]}");
        if (dai.Count == 0) { gui("het", "{\"so\":0,\"loi\":\"khong-mang\"}"); return; }

        List<May> thay = DoMang(dai, gui).Result;
        lock (mayDaThay) { mayDaThay.Clear(); foreach (May m in thay) mayDaThay[m.Ip] = m; }

        // Máy sẵn sàng thì tạo icon ngay, khỏi chờ khách bấm.
        var sanSang = thay.Where(m => m.TrangThai == "san-sang").ToList();
        var icon = new List<string>();
        foreach (May m in sanSang)
        {
            try { icon.Add(TaoIcon(m, sanSang.Count > 1)); }
            catch (Exception e) { Log("Không tạo được icon: " + e.Message); }
        }
        if (sanSang.Count == 1) MoTrinhDuyet(sanSang[0].Url);

        gui("het", "{\"so\":" + thay.Count + ",\"icon\":[" + string.Join(",", icon.Select(Js)) + "],\"daMo\":" +
            (sanSang.Count == 1 ? Js(sanSang[0].Url) : "null") + "}");
    }

    static List<Dai> LayDaiMang()
    {
        var kq = new List<Dai>();
        var daCo = new HashSet<string>();
        string[] boQua = { "vpn", "virtual", "vmware", "virtualbox", "hyper-v", "tap-", "radmin", "zerotier", "tailscale", "wireguard", "bluetooth", "loopback" };
        var nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 1 : 0);   // mạng dây trước
        foreach (NetworkInterface n in nics)
        {
            string moTa = (n.Description + " " + n.Name).ToLowerInvariant();
            if (boQua.Any(moTa.Contains)) continue;
            foreach (UnicastIPAddressInformation a in n.GetIPProperties().UnicastAddresses)
            {
                if (a.Address.AddressFamily != AddressFamily.InterNetwork || a.IPv4Mask == null) continue;
                byte[] ip = a.Address.GetAddressBytes();
                if (ip[0] == 169 && ip[1] == 254) continue;
                int tien = a.IPv4Mask.GetAddressBytes().Sum(b => Convert.ToString(b, 2).Count(c => c == '1'));
                if (tien < 22) tien = 24;   // mạng rộng (/16, /8) thì chỉ dò quanh máy này
                if (tien > 30) continue;
                Dai d = DaiTuChuoi(a.Address + "/" + tien);
                if (!daCo.Add(d.Mang)) continue;
                d.Ten = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "mạng dây";
                if (thuMucThu == null) d.Host.RemoveAll(h => h.Equals(a.Address));
                kq.Add(d);
            }
        }
        return kq;
    }

    static Dai DaiTuChuoi(string cidr)
    {
        string[] p = cidr.Split('/');
        int tien = p.Length > 1 ? int.Parse(p[1]) : 24;
        uint ip = SoTuIp(IPAddress.Parse(p[0]));
        uint mat = tien == 0 ? 0 : uint.MaxValue << (32 - tien);
        uint mang = ip & mat, cuoi = mang | ~mat;
        var d = new Dai { Ten = "dải " + cidr, Mang = IpTuSo(mang) + "/" + tien };
        for (uint h = mang + 1; h < cuoi; h++) d.Host.Add(IPAddress.Parse(IpTuSo(h)));
        return d;
    }

    static uint SoTuIp(IPAddress a) { byte[] b = a.GetAddressBytes(); return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3]; }
    static string IpTuSo(uint v) { return (v >> 24) + "." + ((v >> 16) & 255) + "." + ((v >> 8) & 255) + "." + (v & 255); }

    static async Task<List<May>> DoMang(IList<Dai> dai, Action<string, string> gui)
    {
        var thay = new List<May>();
        var hen = new SemaphoreSlim(192);
        int tong = dai.Sum(d => d.Host.Count), xong = 0;
        var viec = new List<Task>();
        foreach (Dai d in dai)
        {
            foreach (IPAddress h in d.Host)
            {
                await hen.WaitAsync();
                Dai dd = d; IPAddress hh = h;
                viec.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (!await CongMo(hh, 443)) return;
                        May m = await NhanDang(hh);
                        if (m == null) return;
                        lock (thay) thay.Add(m);
                        Log("Thấy máy " + m.Ip + " " + m.TrangThai + " (HTTP " + m.MaHttp + ")");
                        if (gui != null) gui("may", JsMay(m));
                    }
                    finally
                    {
                        hen.Release();
                        int n = Interlocked.Increment(ref xong);
                        if (gui != null && (n % 8 == 0 || n == tong))
                            gui("tien", "{\"xong\":" + n + ",\"tong\":" + tong + ",\"dai\":" + Js(dd.Ten + " " + dd.Mang) + "}");
                    }
                }));
            }
        }
        await Task.WhenAll(viec);
        Log("Dò xong " + tong + " địa chỉ, thấy " + thay.Count + " máy");
        return thay.OrderBy(m => SoTuIp(IPAddress.Parse(m.Ip))).ToList();
    }

    static async Task<bool> CongMo(IPAddress ip, int c)
    {
        using (var t = new TcpClient(AddressFamily.InterNetwork))
        {
            try
            {
                Task noi = t.ConnectAsync(ip, c);
                if (await Task.WhenAny(noi, Task.Delay(ChoNoiMs)) != noi) return false;
                await noi;
                return t.Connected;
            }
            catch { return false; }
        }
    }

    // Màn hình máy rang OTL là panel Siemens WinCC Unified: mở 443 (web), 102 (S7), 5900 (VNC).
    // PLC S7 cũng mở 102 (+443 nếu bật web server) nhưng không có VNC và không có trang WebRH.
    static async Task<May> NhanDang(IPAddress ip)
    {
        Task<bool> s7 = CongMo(ip, 102), vnc = CongMo(ip, 5900);
        int maCuoi = 0;
        foreach (string duong in DuongManHinh)
        {
            int ma = 0; string noiDung = "";
            try
            {
                var r = (HttpWebRequest)WebRequest.Create("https://" + ip + duong);
                r.Timeout = 4000; r.ReadWriteTimeout = 4000; r.AllowAutoRedirect = false;
                r.UserAgent = "OTL-Tim-May-Rang/" + Ban;
                using (var p = (HttpWebResponse)await LayTraLoi(r)) { ma = (int)p.StatusCode; noiDung = DocDauTraLoi(p); }
            }
            catch (WebException e)
            {
                var p = e.Response as HttpWebResponse;
                if (p != null) { ma = (int)p.StatusCode; noiDung = DocDauTraLoi(p); p.Close(); }
                else Log("Hỏi " + ip + duong + " lỗi: " + e.Status + " " + e.Message);
            }
            catch (Exception e) { Log("Hỏi " + ip + duong + " lỗi: " + e.Message); }
            if (maCuoi == 0) maCuoi = ma;
            // Web client bật thì trang trả "<title>WinCC Unified RT</title>" (đo trên MTP1200 và Unified PC RT V19).
            string t = noiDung.ToLowerInvariant();
            if (ma >= 200 && ma < 500 && ma != 404 && (t.Contains("wincc unified") || t.Contains("webrh")))
                return new May { Ip = ip.ToString(), Url = "https://" + ip + duong, MaHttp = ma, TrangThai = "san-sang" };
        }
        // Không đường nào ra trang runtime mà cổng S7 + VNC của panel vẫn mở = panel chưa bật Web client.
        if (await s7 && await vnc)
            return new May { Ip = ip.ToString(), Url = "https://" + ip + DuongManHinh[0], MaHttp = maCuoi, TrangThai = "chua-bat-web" };
        return null;
    }

    static Task<WebResponse> LayTraLoi(HttpWebRequest r)
    {
        return Task.Factory.FromAsync<WebResponse>(r.BeginGetResponse, r.EndGetResponse, null);
    }

    static string DocDauTraLoi(HttpWebResponse p)
    {
        var sb = new StringBuilder();
        foreach (string k in p.Headers.AllKeys) sb.Append(k).Append(": ").Append(p.Headers[k]).Append('\n');
        try
        {
            using (Stream st = p.GetResponseStream())
            {
                byte[] b = new byte[8192];
                int n = st.Read(b, 0, b.Length);
                sb.Append(Encoding.UTF8.GetString(b, 0, Math.Max(n, 0)));
            }
        }
        catch { }
        return sb.ToString();
    }

    // ── /api/thu?ip=… : khách tự nhập IP ghi trên màn hình máy ───────────────────────────────────

    static void Thu(NetworkStream s, Dictionary<string, string> ts)
    {
        IPAddress ip;
        string v;
        if (!ts.TryGetValue("ip", out v) || !LaIpNoiBo(v, out ip)) { GuiChu(s, 200, "application/json", "{\"loi\":\"ip-sai\"}"); return; }
        if (!CongMo(ip, 443).Result) { GuiChu(s, 200, "application/json", "{\"loi\":\"khong-tra-loi\"}"); return; }
        May m = NhanDang(ip).Result;
        if (m == null) { GuiChu(s, 200, "application/json", "{\"loi\":\"khong-phai-may\"}"); return; }
        lock (mayDaThay) mayDaThay[m.Ip] = m;
        GuiChu(s, 200, "application/json", JsMay(m));
    }

    static bool LaIpNoiBo(string v, out IPAddress ip)
    {
        if (!IPAddress.TryParse((v ?? "").Trim(), out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    // ── /api/mo?ip=… : mở màn hình máy + tạo icon desktop ────────────────────────────────────────

    static void Mo(NetworkStream s, Dictionary<string, string> ts)
    {
        string ip;
        May m = null;
        if (ts.TryGetValue("ip", out ip)) lock (mayDaThay) mayDaThay.TryGetValue(ip, out m);
        if (m == null) { GuiChu(s, 200, "application/json", "{\"loi\":\"khong-co-may\"}"); return; }
        int soMay;
        lock (mayDaThay) soMay = mayDaThay.Values.Count(x => x.TrangThai == "san-sang");
        string icon = null;
        try { icon = TaoIcon(m, soMay > 1); } catch (Exception e) { Log("Không tạo được icon: " + e.Message); }
        MoTrinhDuyet(m.Url);
        GuiChu(s, 200, "application/json", "{\"daMo\":" + Js(m.Url) + ",\"icon\":" + (icon == null ? "null" : Js(icon)) + "}");
    }

    static string TaoIcon(May m, bool nhieuMay)
    {
        string desktop = thuMucThu ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string ten = nhieuMay ? "Máy rang OTL (" + m.Ip + ")" : "Máy rang OTL";
        string ico = Path.Combine(thuMucApp, "may-rang-otl.ico");
        if (!File.Exists(ico)) File.WriteAllBytes(ico, TaiNguyen("may-rang-otl.ico"));
        string tep = Path.Combine(desktop, ten + ".url");
        // .url là tệp INI; Windows đọc theo bảng mã ANSI.
        File.WriteAllText(tep, "[InternetShortcut]\r\nURL=" + m.Url + "\r\nIconFile=" + ico + "\r\nIconIndex=0\r\n", Encoding.Default);
        SHChangeNotify(0x00002000 /*SHCNE_UPDATEITEM*/, 0x0005 /*SHCNF_PATHW*/, tep, IntPtr.Zero);
        Log("Tạo icon " + tep + " → " + m.Url);
        return ten;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern void SHChangeNotify(int eventId, int flags, string item1, IntPtr item2);

    // ── Tiện ích ─────────────────────────────────────────────────────────────────────────────────

    static bool LaAppDangChay(int c)
    {
        try
        {
            var r = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + c + "/api/ten");
            r.Timeout = 1500;
            using (var p = (HttpWebResponse)r.GetResponse())
            using (var sr = new StreamReader(p.GetResponseStream())) return sr.ReadToEnd().Contains("otl-tim-may-rang");
        }
        catch { return false; }
    }

    static void MoTrinhDuyet(string url)
    {
        if (thuMucThu != null) { Log("Mở trình duyệt (bài thử, bỏ qua): " + url); return; }
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Log("Không mở được trình duyệt: " + e.Message); }
    }

    static string JsMay(May m)
    {
        return "{\"ip\":" + Js(m.Ip) + ",\"url\":" + Js(m.Url) + ",\"trangThai\":" + Js(m.TrangThai) + ",\"maHttp\":" + m.MaHttp + "}";
    }

    static string Js(string v)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in v)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static void Log(string dong)
    {
        lock (khoaLog)
        {
            try { File.AppendAllText(Path.Combine(thuMucApp, "nhat-ky.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + dong + "\r\n", Encoding.UTF8); }
            catch { }
        }
    }
}
