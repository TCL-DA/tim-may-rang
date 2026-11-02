// OTL Tìm máy rang — dò máy rang trong mạng LAN, mở màn hình máy toàn màn hình, tạo icon ngoài desktop.
//
// Không có giao diện riêng. Khách dùng trang https://tcl-da.github.io/tim-may-rang/: nút "Tìm máy rang" gọi
// otl-tim://tim, Windows chạy exe này. Exe làm phần trình duyệt bị cấm (quét LAN, ghim chứng chỉ tự ký, ghi
// icon ra desktop) rồi:
//   - đúng một máy sẵn sàng → mở màn hình máy toàn màn hình, xong, không mở thêm gì;
//   - còn lại (không thấy, nhiều máy, panel chưa bật web…) → mở lại trang GitHub với #ket=… để trang báo.
// Lần đầu khách bấm đúp exe tải về: exe tự chép vào %LOCALAPPDATA%\Programs, đăng ký otl-tim:// (HKCU,
// không cần quyền admin) rồi tìm máy luôn.
//
// Viết theo C# 5 để csc có sẵn trong .NET Framework 4.x biên dịch được — máy khách không cần cài gì.
// Build: app\build.ps1

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

[assembly: AssemblyTitle("OTL Tìm máy rang")]
[assembly: AssemblyProduct("OTL Tìm máy rang")]
[assembly: AssemblyCompany("OTL Roaster")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

static class TimMayRang
{
    const string Ban = "1.3";
    const string GiaoThuc = "otl-tim";
    const string TrangWeb = "https://tcl-da.github.io/tim-may-rang/";
    // Chờ một cổng TCP trả lời. Trong LAN máy có thật trả lời (SYN-ACK hoặc RST) trong vài ms; chỉ địa chỉ
    // trống mới chờ hết, và mọi địa chỉ được thử cùng lúc nên cả lượt quét chỉ tốn chừng này.
    const int ChoNoiMs = 400;
    // Lượt nhanh không thấy máy nào thì dò lại với thời gian chờ này: máy Wi-Fi đang ngủ đo được ~1,5 s.
    const int ChoNoiChamMs = 1500;
    const int ChoHttpMs = 2500;         // thiết bị lạ có cổng 443 (router, máy in) đừng giữ lượt dò lâu
    // Trang web client WinCC Unified: panel Unified Comfort để ở /device/WebRH (/WebRH của panel là 503
    // của Industrial Edge, đo trên MTP1200 02/11/2026); Unified PC Runtime để ở /WebRH.
    static readonly string[] DuongManHinh = { "/device/WebRH", "/WebRH" };

    static string thuMucApp;
    static string thuMucThu;            // khác null khi chạy --thu: không mở trình duyệt, icon ghi vào thư mục này

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
        Log("Khởi động bản " + Ban + " " + string.Join(" ", args));
        // Lỗi nào lọt khỏi các chỗ đã bắt cũng phải vào lịch sử.
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            GhiLoi("Lỗi chương trình", e.ExceptionObject is Exception ? MoTaLoi((Exception)e.ExceptionObject) : "" + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (s, e) => { GhiLoi("Lỗi chương trình", MoTaLoi(e.Exception)); e.SetObserved(); };

        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; }; // màn hình máy dùng chứng chỉ tự ký
        // Panel MTP1200 chỉ bắt tay TLS 1.3 (chỉ bật Tls12 thì mọi yêu cầu tới panel hỏng).
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; }   // TLS 1.3, có từ .NET 4.8 trên Windows 11
        catch (NotSupportedException) { }
        ServicePointManager.DefaultConnectionLimit = 64;
        ThreadPool.SetMinThreads(256, 256);   // 508 kết nối đi cùng lúc: đừng chờ thread pool tăng dần

        var ds = new List<string>(args);
        if (ds.Count >= 2 && ds[0] == "--do")   // chỉ dò, kết quả vào nhật ký: --do 192.168.0.0/24
        {
            DoMang(new[] { DaiTuChuoi(ds[1]) }, ChoNoiMs).Wait();
            return 0;
        }
        if (ds.Count >= 2 && ds[0] == "--thu") { thuMucThu = ds[1]; ds.RemoveRange(0, 2); }

        if (thuMucThu == null)
        {
            try { CaiDat(); } catch (Exception e) { GhiLoi("Cài phần mềm", "Không cài được: " + MoTaLoi(e)); }
            if (ds.Count > 0 && ds[0] == "--cai") return 0;   // chỉ cài, không dò
        }

        // Hai lần bấm dồn thì lần sau thôi — một lượt dò đã đủ.
        bool moi;
        using (var khoa = new Mutex(true, @"Local\OTL-Tim-May-Rang", out moi))
        {
            if (!moi) { Log("Đang có một lượt khác, bỏ qua"); return 0; }
            string lenh = ds.Count > 0 ? ds[0] : GiaoThuc + "://tim";
            try
            {
                if (lenh.StartsWith(GiaoThuc + ":", StringComparison.OrdinalIgnoreCase)) ChayLenh(lenh);
                else GhiLoi("Lỗi chương trình", "Không hiểu lệnh: " + lenh);
            }
            catch (Exception e) { GhiLoi("Lỗi chương trình", MoTaLoi(e)); }
            if (thuMucThu == null && !daGui) GuiLen();   // lần nào chạy cũng gửi những mục còn chờ
        }
        return 0;
    }

    // otl-tim://tim          dò cả mạng
    // otl-tim://mo/<ip>      kiểm một địa chỉ (khách tự nhập, hoặc chọn một trong nhiều máy) rồi mở
    // otl-tim://lich-su      mở trang lịch sử lỗi + thao tác
    static void ChayLenh(string lenh)
    {
        string than = lenh.Substring(GiaoThuc.Length + 1).Trim('/', ' ');
        string[] p = than.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string viec = p.Length > 0 ? p[0].ToLowerInvariant() : "tim";
        if (viec == "mo") Mo(p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");
        else if (viec == "lich-su") MoLichSu();
        else Tim();
    }

    static void Tim()
    {
        var dongHo = Stopwatch.StartNew();
        List<Dai> dai = LayDaiMang();
        string mang = string.Join(", ", dai.Select(x => x.Ten + " " + x.Mang));
        Log("Dò " + dai.Count + " dải: " + mang);
        if (dai.Count == 0) { GhiLoi("Tìm máy rang", "Máy tính chưa nối mạng nào (Wi-Fi hoặc dây mạng)."); BaoTrang("ket=khong-mang"); return; }

        List<May> thay = DoMang(dai, ChoNoiMs).Result;
        if (thay.Count == 0) { Log("Lượt nhanh không thấy máy, dò lại chậm"); thay = DoMang(dai, ChoNoiChamMs).Result; }
        var sanSang = thay.Where(m => m.TrangThai == "san-sang").ToList();
        var chuaBat = thay.Where(m => m.TrangThai != "san-sang").ToList();
        long ms = dongHo.ElapsedMilliseconds;
        foreach (May m in sanSang) TaoIcon(m, sanSang.Count > 1);

        if (chuaBat.Count > 0)
            GhiLoi("Tìm máy rang", "Thấy màn hình máy ở " + DanhSach(chuaBat) + " nhưng chưa bật chế độ xem từ máy tính.", ms);
        if (sanSang.Count == 1)
        {
            GhiThaoTac("Tìm máy rang", "Thấy máy ở " + sanSang[0].Ip + ", mở màn hình máy.", ms);
            MoMay(sanSang[0]);
            return;
        }
        if (sanSang.Count > 1)
        {
            GhiThaoTac("Tìm máy rang", "Thấy " + sanSang.Count + " máy: " + DanhSach(sanSang) + ". Mỗi máy một icon, chờ chọn máy.", ms);
            BaoTrang("ket=nhieu&may=" + DanhSach(sanSang) + (chuaBat.Count > 0 ? "&chua=" + DanhSach(chuaBat) : ""));
            return;
        }
        if (chuaBat.Count > 0) { BaoTrang("ket=chua-bat&chua=" + DanhSach(chuaBat)); return; }
        GhiLoi("Tìm máy rang", "Không thấy máy rang nào. Đã dò " + mang + ".", ms);
        BaoTrang("ket=khong-thay&mang=" + Uri.EscapeDataString(mang));
    }

    static void Mo(string v)
    {
        IPAddress ip;
        string viec = "Mở máy " + v;
        if (!LaIpNoiBo(v, out ip)) { GhiLoi(viec, "Không phải địa chỉ IP mạng nội bộ (192.168.x.x, 10.x.x.x, 172.16–31.x.x)."); BaoTrang("ket=ip-sai&ip=" + Uri.EscapeDataString(v)); return; }
        if (!CongMo(ip, 443, ChoNoiChamMs).Result) { GhiLoi(viec, "Địa chỉ không trả lời."); BaoTrang("ket=khong-tra-loi&ip=" + ip); return; }
        May m = NhanDang(ip, ChoNoiChamMs).Result;
        if (m == null) { GhiLoi(viec, "Có trả lời nhưng không phải màn hình máy rang."); BaoTrang("ket=khong-phai-may&ip=" + ip); return; }
        if (m.TrangThai != "san-sang") { GhiLoi(viec, "Màn hình máy chưa bật chế độ xem từ máy tính."); BaoTrang("ket=chua-bat&chua=" + ip); return; }
        TaoIcon(m, false);
        GhiThaoTac(viec, "Mở màn hình máy.");
        MoMay(m);
    }

    static string DanhSach(IEnumerable<May> ds) { return string.Join(",", ds.Select(m => m.Ip)); }

    static void BaoTrang(string ket)
    {
        Log("Báo trang: " + ket);
        MoTrinhDuyet(TrangWeb + "#" + ket);
    }

    // ── Cài đặt: chép exe vào chỗ cố định + đăng ký otl-tim:// cho người dùng hiện tại ──────────

    static readonly string ExeCai = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "OTL Tim May Rang", "OTL Tim May Rang.exe");

    static void CaiDat()
    {
        string exeDich = ExeCai, dich = Path.GetDirectoryName(ExeCai);
        string exeNay = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(Path.GetFullPath(exeNay), Path.GetFullPath(exeDich), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(dich);
            if (!File.Exists(exeDich) || !File.ReadAllBytes(exeDich).SequenceEqual(File.ReadAllBytes(exeNay)))
            {
                bool capNhat = File.Exists(exeDich);
                File.Copy(exeNay, exeDich, true);
                Log("Đã chép phần mềm vào " + exeDich);
                GhiThaoTac("Cài phần mềm", (capNhat ? "Cập nhật lên bản " : "Cài bản ") + Ban + " cho máy tính này.");
            }
        }
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + GiaoThuc))
        {
            k.SetValue("", "URL:OTL Tim may rang");
            k.SetValue("URL Protocol", "");
            using (RegistryKey i = k.CreateSubKey("DefaultIcon")) i.SetValue("", "\"" + exeDich + "\",0");
            using (RegistryKey c = k.CreateSubKey(@"shell\open\command")) c.SetValue("", "\"" + exeDich + "\" \"%1\"");
        }
    }

    // ── Dò mạng ──────────────────────────────────────────────────────────────────────────────────

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

    static async Task<List<May>> DoMang(IList<Dai> dai, int choNoi)
    {
        var thay = new List<May>();
        var dongHo = Stopwatch.StartNew();
        var hen = new SemaphoreSlim(1024);   // hai dải /24 = 508 địa chỉ đi cùng một lượt
        var viec = new List<Task>();
        foreach (Dai d in dai)
        {
            foreach (IPAddress h in d.Host)
            {
                await hen.WaitAsync();
                IPAddress hh = h;
                viec.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (!await CongMo(hh, 443, choNoi)) return;
                        May m = await NhanDang(hh, choNoi);
                        if (m == null) return;
                        lock (thay) thay.Add(m);
                        Log("Thấy máy " + m.Ip + " " + m.TrangThai + " (HTTP " + m.MaHttp + ")");
                    }
                    finally { hen.Release(); }
                }));
            }
        }
        // Đã thấy máy thì không chờ hết các thiết bị lạ bắt tay HTTPS chậm (router, máy Wi-Fi ngủ — đo được
        // ~1 s): panel trả lời trong vài chục ms, nên quét xong mà thêm 0,3 s không ra máy nào nữa là đủ.
        // Chưa thấy máy nào thì chờ đủ, đừng bỏ sót.
        Task tatCa = Task.WhenAll(viec);
        while (!tatCa.IsCompleted)
        {
            await Task.WhenAny(tatCa, Task.Delay(50));
            bool coMay;
            lock (thay) coMay = thay.Any(m => m.TrangThai == "san-sang");
            if (coMay && dongHo.ElapsedMilliseconds > choNoi + 300) { Log("Đã thấy máy, thôi chờ thiết bị chậm"); break; }
        }
        List<May> kq;
        lock (thay) kq = thay.OrderBy(m => SoTuIp(IPAddress.Parse(m.Ip))).ToList();
        Log("Dò xong " + dai.Sum(d => d.Host.Count) + " địa chỉ, thấy " + kq.Count + " máy");
        return kq;
    }

    static async Task<bool> CongMo(IPAddress ip, int c, int choNoi)
    {
        using (var t = new TcpClient(AddressFamily.InterNetwork))
        {
            try
            {
                Task noi = t.ConnectAsync(ip, c);
                if (await Task.WhenAny(noi, Task.Delay(choNoi)) != noi) return false;
                await noi;
                return t.Connected;
            }
            catch { return false; }
        }
    }

    // Màn hình máy rang OTL là panel Siemens WinCC Unified: mở 443 (web), 102 (S7), 5900 (VNC).
    // PLC S7 cũng mở 102 (+443 nếu bật web server) nhưng không có VNC và không có trang WebRH.
    static async Task<May> NhanDang(IPAddress ip, int choNoi)
    {
        Task<bool> s7 = CongMo(ip, 102, choNoi), vnc = CongMo(ip, 5900, choNoi);
        int maCuoi = 0;
        foreach (string duong in DuongManHinh)
        {
            int ma = 0; string noiDung = "";
            try
            {
                var r = (HttpWebRequest)WebRequest.Create("https://" + ip + duong);
                r.Timeout = ChoHttpMs; r.ReadWriteTimeout = ChoHttpMs; r.AllowAutoRedirect = false;
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

    static bool LaIpNoiBo(string v, out IPAddress ip)
    {
        if (!IPAddress.TryParse((v ?? "").Trim(), out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    // ── Icon desktop ─────────────────────────────────────────────────────────────────────────────
    // .lnk gọi lại chính phần mềm đã cài với otl-tim://mo/<ip>, không gọi thẳng Edge: bấm icon khi cửa sổ
    // máy đang mở thì phải đưa cửa sổ đó lên (MoMay), và chứng chỉ panel đổi thì lần mở sau ghim lại đúng.

    static void TaoIcon(May m, bool nhieuMay)
    {
        string ten = nhieuMay ? "Máy rang OTL (" + m.Ip + ")" : "Máy rang OTL";
        try { TaoIcon(m, ten); }
        catch (Exception e) { GhiLoi("Tạo icon", "Không tạo được icon \"" + ten + "\" ngoài desktop: " + MoTaLoi(e)); }
    }

    static void TaoIcon(May m, string ten)
    {
        string desktop = thuMucThu ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string ico = Path.Combine(thuMucApp, "may-rang-otl.ico");
        if (!File.Exists(ico)) File.WriteAllBytes(ico, TaiNguyen("may-rang-otl.ico"));
        string url = Path.Combine(desktop, ten + ".url"), lnk = Path.Combine(desktop, ten + ".lnk");
        if (File.Exists(url)) File.Delete(url);   // icon bản 1.0
        bool daCo = File.Exists(lnk);
        Type loai = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(loai);
        try
        {
            object sc = loai.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type t = sc.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { ExeCai });
            t.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { GiaoThuc + "://mo/" + m.Ip });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { ico + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Màn hình máy rang OTL " + m.Ip });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            Marshal.FinalReleaseComObject(sc);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        SHChangeNotify(0x00002000 /*SHCNE_UPDATEITEM*/, 0x0005 /*SHCNF_PATHW*/, lnk, IntPtr.Zero);
        Log("Tạo icon " + lnk + " → " + m.Url);
        if (!daCo) GhiThaoTac("Tạo icon", "Tạo icon \"" + ten + "\" ngoài desktop, mở máy " + m.Ip + ".");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern void SHChangeNotify(int eventId, int flags, string item1, IntPtr item2);

    static byte[] TaiNguyen(string ten)
    {
        using (Stream r = Assembly.GetExecutingAssembly().GetManifestResourceStream(ten))
        {
            var m = new MemoryStream();
            r.CopyTo(m);
            return m.ToArray();
        }
    }

    // ── Cửa sổ màn hình máy: Edge/Chrome chế độ ứng dụng, toàn màn hình ──────────────────────────
    // Hồ sơ trình duyệt riêng (--user-data-dir) để cờ dòng lệnh luôn có hiệu lực dù trình duyệt của khách
    // đang mở, và để ghim chứng chỉ tự ký của đúng panel này (--ignore-certificate-errors-spki-list chỉ
    // chạy khi có --user-data-dir) — khách không gặp trang "kết nối không riêng tư".

    static string TimTrinhDuyet()
    {
        foreach (string ten in new[] { "msedge.exe", "chrome.exe" })
            foreach (RegistryKey goc in new[] { Registry.CurrentUser, Registry.LocalMachine })
                using (RegistryKey k = goc.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + ten))
                {
                    string v = k == null ? null : k.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(v) && File.Exists(v.Trim('"'))) return v.Trim('"');
                }
        return null;
    }

    // Mỗi máy một hồ sơ: mở máy này không đá cửa sổ máy kia, đăng nhập WebRH nhớ riêng từng máy.
    static string HoSo(May m) { return Path.Combine(thuMucApp, "trinh-duyet", m.Ip); }

    // --kiosk chứ không --app --start-fullscreen: hồ sơ đã nhớ khung cửa sổ của lần trước thì Edge bỏ qua
    // --start-fullscreen (đo 02/11/2026: mở lần 2 ra nửa màn hình); kiosk lần nào cũng phủ kín. Đóng bằng Alt+F4.
    // --test-type: tắt thanh "You're using an unsupported command-line flag" Edge hiện vì cờ ghim chứng chỉ.
    static string ThamSoManHinh(May m)
    {
        string a = "--kiosk \"" + m.Url + "\" --edge-kiosk-type=fullscreen --user-data-dir=\"" + HoSo(m) + "\" --no-first-run --no-default-browser-check --test-type";
        string spki = null;
        try { spki = SpkiChungChi(m.Ip); }
        catch (Exception e) { GhiLoi("Mở màn hình máy", "Không đọc được chứng chỉ của máy " + m.Ip + ", trình duyệt có thể báo kết nối không riêng tư: " + MoTaLoi(e)); }
        if (spki != null) a += " --ignore-certificate-errors-spki-list=" + spki;
        return a;
    }

    // Edge của hồ sơ này đang chạy thì lệnh mở mới bị giao cho tiến trình cũ và mất --start-fullscreen (khách
    // chụp được: cửa sổ có thanh tiêu đề). Có cửa sổ thì đưa nó lên, không mở thêm; chạy ngầm không cửa sổ thì
    // tắt để lần mở này nhận đủ cờ.
    static bool DuaCuaSoCuLen(string hoSo)
    {
        string dau = "--user-data-dir=\"" + hoSo + "\"";
        var pid = new List<int>();
        using (var s = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='msedge.exe' OR Name='chrome.exe'"))
            foreach (ManagementBaseObject o in s.Get())
            {
                string cl = o["CommandLine"] as string;
                if (cl != null && cl.IndexOf(dau, StringComparison.OrdinalIgnoreCase) >= 0) pid.Add(Convert.ToInt32(o["ProcessId"]));
            }
        foreach (int id in pid)
        {
            try
            {
                IntPtr h = Process.GetProcessById(id).MainWindowHandle;
                if (h == IntPtr.Zero) continue;
                if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
                SetForegroundWindow(h);
                Log("Màn hình máy đang mở, đưa lên trước (pid " + id + ")");
                GhiThaoTac("Mở màn hình máy", "Màn hình máy đang mở sẵn, đưa cửa sổ lên trước.");
                return true;
            }
            catch (ArgumentException) { }   // tiến trình vừa thoát
        }
        if (pid.Count > 0)
        {
            foreach (int id in pid) { try { Process.GetProcessById(id).Kill(); } catch (ArgumentException) { } catch (InvalidOperationException) { } }
            Log("Tắt " + pid.Count + " tiến trình Edge chạy ngầm của hồ sơ máy");
            Thread.Sleep(500);
        }
        return false;
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int lenh);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);

    static void MoMay(May m)
    {
        string trinhDuyet = TimTrinhDuyet();
        if (trinhDuyet == null)
        {
            GhiLoi("Mở màn hình máy", "Máy tính không có Edge hoặc Chrome, mở bằng trình duyệt mặc định (không toàn màn hình).");
            MoTrinhDuyet(m.Url);
            return;
        }
        if (thuMucThu == null)
        {
            try { if (DuaCuaSoCuLen(HoSo(m))) return; }
            catch (Exception e) { GhiLoi("Mở màn hình máy", "Không kiểm được cửa sổ máy đang mở: " + MoTaLoi(e)); }
        }
        string thamSo = ThamSoManHinh(m);
        if (thuMucThu != null) { Log("Mở màn hình máy (bài thử, bỏ qua): " + trinhDuyet + " " + thamSo); return; }
        try { Process.Start(trinhDuyet, thamSo); Log("Mở màn hình máy " + m.Url); }
        catch (Exception e)
        {
            GhiLoi("Mở màn hình máy", "Không mở được " + Path.GetFileName(trinhDuyet) + ", thử trình duyệt mặc định: " + MoTaLoi(e));
            MoTrinhDuyet(m.Url);
        }
    }

    // SHA-256 (base64) của SubjectPublicKeyInfo trong chứng chỉ máy đang dùng — dạng Chromium cần.
    static string SpkiChungChi(string ip)
    {
        X509Certificate2 cc = null;
        using (var t = new TcpClient(AddressFamily.InterNetwork))
        {
            if (!t.ConnectAsync(ip, 443).Wait(3000)) return null;
            using (var ssl = new SslStream(t.GetStream(), false, delegate (object o, X509Certificate c, X509Chain ch, SslPolicyErrors e)
                { cc = new X509Certificate2(c); return true; }))
            {
                ssl.ReadTimeout = ssl.WriteTimeout = 4000;
                ssl.AuthenticateAsClient(ip, null, SslProtocols.Tls12 | (SslProtocols)12288 /*Tls13*/, false);
            }
        }
        if (cc == null) return null;
        // Certificate ::= SEQ { tbsCertificate SEQ { [0] version?, serial, sigAlg, issuer, validity, subject, spki, … } … }
        byte[] d = cc.RawData;
        int p = 0;
        DocDauDer(d, ref p);                       // vào Certificate
        DocDauDer(d, ref p);                       // vào tbsCertificate
        if (d[p] == 0xA0) BoQuaDer(d, ref p);      // version
        for (int i = 0; i < 5; i++) BoQuaDer(d, ref p);
        int dau = p;
        BoQuaDer(d, ref p);
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(d, dau, p - dau));
    }

    static int DocDauDer(byte[] d, ref int p)
    {
        p++;                                       // tag
        int n = d[p++];
        if (n >= 0x80) { int k = n & 0x7F; n = 0; while (k-- > 0) n = (n << 8) | d[p++]; }
        return n;
    }

    static void BoQuaDer(byte[] d, ref int p) { int n = DocDauDer(d, ref p); p += n; }

    // ── Tiện ích ─────────────────────────────────────────────────────────────────────────────────

    static void MoTrinhDuyet(string url)
    {
        if (thuMucThu != null) { Log("Mở trình duyệt (bài thử, bỏ qua): " + url); return; }
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { GhiLoi("Mở trình duyệt", "Không mở được trình duyệt mặc định: " + MoTaLoi(e)); }
    }

    // ── Lịch sử lỗi + thao tác ──────────────────────────────────────────────────────────────────
    // lich-su.jsonl: mỗi dòng một mục {t, loai: "loi"|"thao-tac", viec, chiTiet, ms}. Khác nhat-ky.txt (dòng kỹ
    // thuật cho người sửa phần mềm): đây là thứ khách đọc được và gửi cho OTL. Giữ 2000 mục mới nhất.

    const int GiuMuc = 2000;

    static void GhiThaoTac(string viec, string chiTiet, long ms = 0) { GhiLichSu("thao-tac", viec, chiTiet, ms); }
    static void GhiLoi(string viec, string chiTiet, long ms = 0) { GhiLichSu("loi", viec, chiTiet, ms); }

    static void GhiLichSu(string loai, string viec, string chiTiet, long ms)
    {
        Log((loai == "loi" ? "LỖI " : "THAO TÁC ") + viec + ": " + chiTiet);
        string dong = "{\"t\":" + Js(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")) + ",\"loai\":" + Js(loai) + ",\"viec\":" + Js(viec) +
            ",\"chiTiet\":" + Js(chiTiet) + (ms > 0 ? ",\"ms\":" + ms : "") + "}";
        lock (khoaLog)
        {
            try
            {
                string tep = Path.Combine(thuMucApp, "lich-su.jsonl");
                File.AppendAllText(tep, dong + "\n", new UTF8Encoding(false));
                if (thuMucThu == null) File.AppendAllText(Path.Combine(thuMucApp, "cho-gui.jsonl"), dong + "\n", new UTF8Encoding(false));
                if (new FileInfo(tep).Length > 600000)
                {
                    string[] tat = File.ReadAllLines(tep, Encoding.UTF8);
                    if (tat.Length > GiuMuc) File.WriteAllLines(tep, tat.Skip(tat.Length - GiuMuc), new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }

    // Trang lịch sử: gửi được lên máy chủ thì mở bản trên web của quán (link này mở ở máy nào cũng xem được);
    // không thì dựng tệp HTML từ mẫu nhúng trong exe + lich-su.jsonl và mở trên máy.
    static void MoLichSu()
    {
        GhiThaoTac("Xem lịch sử", "Mở trang lịch sử lỗi và thao tác.");
        if (thuMucThu == null && GuiLen())
        {
            Quan q = LayQuan();
            MoTrinhDuyet(TrangWeb + "lich-su.html#ma=" + q.Ma + "&k=" + q.KhoaXem);
            return;
        }
        string tep = Path.Combine(thuMucApp, "lich-su.jsonl");
        string[] dong;
        lock (khoaLog) dong = File.Exists(tep) ? File.ReadAllLines(tep, Encoding.UTF8) : new string[0];
        string json = "[" + string.Join(",", dong.Where(d => d.StartsWith("{"))) + "]";
        string mau = Encoding.UTF8.GetString(TaiNguyen("lich-su.html"));
        string html = Path.Combine(thuMucApp, "lich-su.html");
        File.WriteAllText(html, mau.Replace("/*DU_LIEU*/[]", json), new UTF8Encoding(false));
        MoTrinhDuyet(html);
    }

    // ── Gửi lịch sử lên máy chủ OTL ─────────────────────────────────────────────────────────────
    // Mỗi máy tính là một "quán": mã + khoá ghi (chỉ phần mềm giữ) + khoá xem (nằm trong link lịch sử của
    // khách), sinh một lần lưu ở quan.txt. Mục mới vào cho-gui.jsonl; gửi được thì xoá, mất mạng thì để lần sau.
    // Địa chỉ máy chủ đọc từ may-chu.json trên trang web, đổi máy chủ không phải phát hành lại phần mềm.

    class Quan { public string Ma, KhoaGhi, KhoaXem; }

    static bool daGui;

    static Quan LayQuan()
    {
        string tep = Path.Combine(thuMucApp, "quan.txt");
        string[] d = File.Exists(tep) ? File.ReadAllLines(tep) : new string[0];
        if (d.Length >= 3 && d[0].Length >= 6) return new Quan { Ma = d[0], KhoaGhi = d[1], KhoaXem = d[2] };
        var q = new Quan { Ma = NgauNhien(8, "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"), KhoaGhi = NgauNhien(32, null), KhoaXem = NgauNhien(24, null) };
        File.WriteAllLines(tep, new[] { q.Ma, q.KhoaGhi, q.KhoaXem });
        return q;
    }

    static string NgauNhien(int n, string bangChu)
    {
        const string macDinh = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string c = bangChu ?? macDinh;
        var b = new byte[n];
        using (var r = new RNGCryptoServiceProvider()) r.GetBytes(b);
        var sb = new StringBuilder();
        foreach (byte x in b) sb.Append(c[x % c.Length]);
        return sb.ToString();
    }

    static string DiaChiMayChu()
    {
        string thu = Environment.GetEnvironmentVariable("OTL_TIM_MAY_CHU");   // bài thử: máy chủ wrangler dev
        if (!string.IsNullOrEmpty(thu)) return thu.TrimEnd('/');
        string luu = Path.Combine(thuMucApp, "may-chu.txt");
        try
        {
            var r = (HttpWebRequest)WebRequest.Create(TrangWeb + "may-chu.json");
            r.Timeout = 3000; r.ReadWriteTimeout = 3000;
            r.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            string j;
            using (var p = (HttpWebResponse)r.GetResponse())
            using (var sr = new StreamReader(p.GetResponseStream(), Encoding.UTF8)) j = sr.ReadToEnd();
            int i = j.IndexOf("\"url\""), a = i < 0 ? -1 : j.IndexOf('"', j.IndexOf(':', i) + 1), b = a < 0 ? -1 : j.IndexOf('"', a + 1);
            string url = b > a ? j.Substring(a + 1, b - a - 1).Trim().TrimEnd('/') : "";
            File.WriteAllText(luu, url);
            return url;
        }
        catch (Exception e)
        {
            Log("Không đọc được may-chu.json: " + e.Message);
            return File.Exists(luu) ? File.ReadAllText(luu).Trim() : "";
        }
    }

    // Trả true khi máy chủ đã có đủ lịch sử của quán (không còn mục chờ).
    static bool GuiLen()
    {
        daGui = true;
        string tep = Path.Combine(thuMucApp, "cho-gui.jsonl");
        string[] cho;
        lock (khoaLog) cho = File.Exists(tep) ? File.ReadAllLines(tep, Encoding.UTF8).Where(d => d.StartsWith("{")).ToArray() : new string[0];
        string url = DiaChiMayChu();
        if (url == "") { Log("Chưa có máy chủ lịch sử (may-chu.json trống)"); return false; }
        Quan q = LayQuan();
        int daNhan = 0;
        bool xong = false;
        try
        {
            do
            {
                string[] lo = cho.Skip(daNhan).Take(400).ToArray();
                string than = "{\"ma\":" + Js(q.Ma) + ",\"khoaGhi\":" + Js(q.KhoaGhi) + ",\"khoaXem\":" + Js(q.KhoaXem) +
                    ",\"may\":" + Js(Environment.MachineName) + ",\"ban\":" + Js(Ban) + ",\"muc\":[" + string.Join(",", lo) + "]}";
                byte[] b = Encoding.UTF8.GetBytes(than);
                var r = (HttpWebRequest)WebRequest.Create(url + "/ghi");
                r.Method = "POST"; r.ContentType = "application/json; charset=utf-8"; r.Timeout = 6000; r.ReadWriteTimeout = 6000;
                using (Stream s = r.GetRequestStream()) s.Write(b, 0, b.Length);
                using (var p = (HttpWebResponse)r.GetResponse()) { }
                daNhan += lo.Length;
            } while (daNhan < cho.Length);
            xong = true;
        }
        catch (Exception e) { Log("Chưa gửi được lịch sử lên máy chủ (" + daNhan + "/" + cho.Length + " mục): " + e.Message); }
        // Xoá đúng những dòng đã gửi; dòng mới ghi thêm trong lúc gửi vẫn ở lại.
        lock (khoaLog)
        {
            try
            {
                if (daNhan > 0)
                {
                    string[] hienTai = File.ReadAllLines(tep, Encoding.UTF8).Where(d => d.StartsWith("{")).ToArray();
                    File.WriteAllLines(tep, hienTai.Skip(daNhan), new UTF8Encoding(false));
                }
            }
            catch (Exception e) { Log("Không dọn được cho-gui.jsonl: " + e.Message); }
        }
        if (daNhan > 0) Log("Đã gửi " + daNhan + " mục lịch sử lên máy chủ");
        return xong;
    }

    static string MoTaLoi(Exception e)
    {
        var a = e as AggregateException;
        if (a != null && a.InnerExceptions.Count == 1) e = a.InnerExceptions[0];
        return e.Message + " (" + e.GetType().Name + ")";
    }

    static string Js(string v)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in v ?? "")
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 32 || c == (char)0x2028 || c == (char)0x2029) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else if (c == '<') sb.Append("\\u003c");   // dữ liệu chèn vào <script>, đừng để "</script>" đóng thẻ
            else sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    static readonly object khoaLog = new object();

    static void Log(string dong)
    {
        lock (khoaLog)
        {
            try { File.AppendAllText(Path.Combine(thuMucApp, "nhat-ky.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + dong + "\r\n", Encoding.UTF8); }
            catch { }
        }
    }
}
