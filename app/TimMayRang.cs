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
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

static class TimMayRang
{
    const string Ban = "1.1";
    const string GiaoThuc = "otl-tim";
    const string TrangWeb = "https://tcl-da.github.io/tim-may-rang/";
    const int ChoNoiMs = 700;           // chờ một cổng TCP trả lời
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

        ServicePointManager.ServerCertificateValidationCallback = delegate { return true; }; // màn hình máy dùng chứng chỉ tự ký
        // Panel MTP1200 chỉ bắt tay TLS 1.3 (chỉ bật Tls12 thì mọi yêu cầu tới panel hỏng).
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288; }   // TLS 1.3, có từ .NET 4.8 trên Windows 11
        catch (NotSupportedException) { }
        ServicePointManager.DefaultConnectionLimit = 64;

        var ds = new List<string>(args);
        if (ds.Count >= 2 && ds[0] == "--do")   // chỉ dò, kết quả vào nhật ký: --do 192.168.0.0/24
        {
            DoMang(new[] { DaiTuChuoi(ds[1]) }).Wait();
            return 0;
        }
        if (ds.Count >= 2 && ds[0] == "--thu") { thuMucThu = ds[1]; ds.RemoveRange(0, 2); }

        if (thuMucThu == null)
        {
            try { CaiDat(); } catch (Exception e) { Log("Không cài được: " + e.Message); }
            if (ds.Count > 0 && ds[0] == "--cai") return 0;   // chỉ cài, không dò
        }

        // Hai lần bấm dồn thì lần sau thôi — một lượt dò đã đủ.
        bool moi;
        using (var khoa = new Mutex(true, @"Local\OTL-Tim-May-Rang", out moi))
        {
            if (!moi) { Log("Đang có một lượt khác, bỏ qua"); return 0; }
            string lenh = ds.Count > 0 ? ds[0] : GiaoThuc + "://tim";
            if (lenh.StartsWith(GiaoThuc + ":", StringComparison.OrdinalIgnoreCase)) ChayLenh(lenh);
            else Log("Không hiểu tham số: " + lenh);
        }
        return 0;
    }

    // otl-tim://tim          dò cả mạng
    // otl-tim://mo/<ip>      kiểm một địa chỉ (khách tự nhập, hoặc chọn một trong nhiều máy) rồi mở
    static void ChayLenh(string lenh)
    {
        string than = lenh.Substring(GiaoThuc.Length + 1).Trim('/', ' ');
        string[] p = than.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        string viec = p.Length > 0 ? p[0].ToLowerInvariant() : "tim";
        if (viec == "mo" && p.Length > 1) Mo(Uri.UnescapeDataString(p[1]));
        else Tim();
    }

    static void Tim()
    {
        List<Dai> dai = LayDaiMang();
        Log("Dò " + dai.Count + " dải: " + string.Join(", ", dai.Select(x => x.Ten + " " + x.Mang)));
        if (dai.Count == 0) { BaoTrang("ket=khong-mang"); return; }

        List<May> thay = DoMang(dai).Result;
        var sanSang = thay.Where(m => m.TrangThai == "san-sang").ToList();
        var chuaBat = thay.Where(m => m.TrangThai != "san-sang").ToList();
        foreach (May m in sanSang)
        {
            try { TaoIcon(m, sanSang.Count > 1); }
            catch (Exception e) { Log("Không tạo được icon: " + e.Message); }
        }

        if (sanSang.Count == 1) { MoMay(sanSang[0]); return; }
        if (sanSang.Count > 1) { BaoTrang("ket=nhieu&may=" + DanhSach(sanSang) + (chuaBat.Count > 0 ? "&chua=" + DanhSach(chuaBat) : "")); return; }
        if (chuaBat.Count > 0) { BaoTrang("ket=chua-bat&chua=" + DanhSach(chuaBat)); return; }
        BaoTrang("ket=khong-thay&mang=" + Uri.EscapeDataString(string.Join(", ", dai.Select(x => x.Ten + " " + x.Mang))));
    }

    static void Mo(string v)
    {
        IPAddress ip;
        if (!LaIpNoiBo(v, out ip)) { BaoTrang("ket=ip-sai&ip=" + Uri.EscapeDataString(v)); return; }
        if (!CongMo(ip, 443).Result) { BaoTrang("ket=khong-tra-loi&ip=" + ip); return; }
        May m = NhanDang(ip).Result;
        if (m == null) { BaoTrang("ket=khong-phai-may&ip=" + ip); return; }
        if (m.TrangThai != "san-sang") { BaoTrang("ket=chua-bat&chua=" + ip); return; }
        try { TaoIcon(m, false); } catch (Exception e) { Log("Không tạo được icon: " + e.Message); }
        MoMay(m);
    }

    static string DanhSach(IEnumerable<May> ds) { return string.Join(",", ds.Select(m => m.Ip)); }

    static void BaoTrang(string ket)
    {
        Log("Báo trang: " + ket);
        MoTrinhDuyet(TrangWeb + "#" + ket);
    }

    // ── Cài đặt: chép exe vào chỗ cố định + đăng ký otl-tim:// cho người dùng hiện tại ──────────

    static void CaiDat()
    {
        string dich = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OTL Tim May Rang");
        string exeDich = Path.Combine(dich, "OTL Tim May Rang.exe");
        string exeNay = Assembly.GetExecutingAssembly().Location;
        if (!string.Equals(Path.GetFullPath(exeNay), Path.GetFullPath(exeDich), StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(dich);
            if (!File.Exists(exeDich) || !File.ReadAllBytes(exeDich).SequenceEqual(File.ReadAllBytes(exeNay)))
            {
                File.Copy(exeNay, exeDich, true);
                Log("Đã chép phần mềm vào " + exeDich);
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

    static async Task<List<May>> DoMang(IList<Dai> dai)
    {
        var thay = new List<May>();
        var hen = new SemaphoreSlim(192);
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
                        if (!await CongMo(hh, 443)) return;
                        May m = await NhanDang(hh);
                        if (m == null) return;
                        lock (thay) thay.Add(m);
                        Log("Thấy máy " + m.Ip + " " + m.TrangThai + " (HTTP " + m.MaHttp + ")");
                    }
                    finally { hen.Release(); }
                }));
            }
        }
        await Task.WhenAll(viec);
        Log("Dò xong " + dai.Sum(d => d.Host.Count) + " địa chỉ, thấy " + thay.Count + " máy");
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

    static bool LaIpNoiBo(string v, out IPAddress ip)
    {
        if (!IPAddress.TryParse((v ?? "").Trim(), out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }

    // ── Icon desktop ─────────────────────────────────────────────────────────────────────────────
    // Có Edge/Chrome thì là .lnk mở thẳng cửa sổ toàn màn hình (ThamSoManHinh); không có thì là .url.

    static string TaoIcon(May m, bool nhieuMay)
    {
        string desktop = thuMucThu ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string ten = nhieuMay ? "Máy rang OTL (" + m.Ip + ")" : "Máy rang OTL";
        string ico = Path.Combine(thuMucApp, "may-rang-otl.ico");
        if (!File.Exists(ico)) File.WriteAllBytes(ico, TaiNguyen("may-rang-otl.ico"));
        string url = Path.Combine(desktop, ten + ".url"), lnk = Path.Combine(desktop, ten + ".lnk");
        string trinhDuyet = TimTrinhDuyet();
        string tep;
        if (trinhDuyet != null)
        {
            if (File.Exists(url)) File.Delete(url);   // icon bản cũ
            Type loai = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(loai);
            try
            {
                object sc = loai.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type t = sc.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { trinhDuyet });
                t.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { ThamSoManHinh(m) });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { ico + ",0" });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Màn hình máy rang OTL " + m.Ip });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.FinalReleaseComObject(sc);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
            tep = lnk;
        }
        else
        {
            if (File.Exists(lnk)) File.Delete(lnk);
            // .url là tệp INI; Windows đọc theo bảng mã ANSI.
            File.WriteAllText(url, "[InternetShortcut]\r\nURL=" + m.Url + "\r\nIconFile=" + ico + "\r\nIconIndex=0\r\n", Encoding.Default);
            tep = url;
        }
        SHChangeNotify(0x00002000 /*SHCNE_UPDATEITEM*/, 0x0005 /*SHCNF_PATHW*/, tep, IntPtr.Zero);
        Log("Tạo icon " + tep + " → " + m.Url);
        return ten;
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

    static string ThamSoManHinh(May m)
    {
        string hoSo = Path.Combine(thuMucApp, "trinh-duyet");
        string a = "--app=\"" + m.Url + "\" --start-fullscreen --user-data-dir=\"" + hoSo + "\" --no-first-run --no-default-browser-check";
        string spki = null;
        try { spki = SpkiChungChi(m.Ip); } catch (Exception e) { Log("Không đọc được chứng chỉ " + m.Ip + ": " + e.Message); }
        if (spki != null) a += " --ignore-certificate-errors-spki-list=" + spki;
        return a;
    }

    static void MoMay(May m)
    {
        string trinhDuyet = TimTrinhDuyet();
        if (trinhDuyet == null) { MoTrinhDuyet(m.Url); return; }
        string thamSo = ThamSoManHinh(m);
        if (thuMucThu != null) { Log("Mở màn hình máy (bài thử, bỏ qua): " + trinhDuyet + " " + thamSo); return; }
        try { Process.Start(trinhDuyet, thamSo); Log("Mở màn hình máy " + m.Url); }
        catch (Exception e) { Log("Không mở được " + trinhDuyet + ": " + e.Message); MoTrinhDuyet(m.Url); }
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
        catch (Exception e) { Log("Không mở được trình duyệt: " + e.Message); }
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
