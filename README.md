<a href="https://tcl-da.github.io/tim-may-rang/"><img src="tai-nguyen/nut-trang-web.svg" alt="Mở trang tìm máy rang" width="560"></a>

# **https://tcl-da.github.io/tim-may-rang/**

<a href="https://tcl-da.github.io/tim-may-rang/OTL-Tim-May-Rang.zip"><img src="tai-nguyen/nut-tai-phan-mem.svg" alt="Tải phần mềm" width="560"></a>

---

## tìm máy rang otl

Trang web cho khách của OTL: bấm **Tìm máy rang**, phần mềm OTL trên máy tính tự dò máy rang trong mạng
của quán, mở màn hình điều khiển của máy toàn màn hình, rồi tạo icon **Máy rang OTL** ngoài desktop. Lần
sau khách chỉ cần bấm đúp icon đó.

Windows 10/11. Lần đầu tải phần mềm và bấm đúp một lần: nó tự cài cho người dùng hiện tại (không cần
quyền admin) và tìm máy luôn.

---

## cách chạy

Trình duyệt không được tự quét mạng trong quán, nên nút trên trang gọi phần mềm đã cài qua giao thức
`otl-tim://`. Không có trang web nội bộ nào.

| Gọi | Việc |
|---|---|
| bấm đúp exe tải về | chép vào `%LOCALAPPDATA%\Programs\OTL Tim May Rang`, đăng ký `otl-tim://` ở `HKCU\Software\Classes`, rồi dò như `otl-tim://tim` |
| `otl-tim://tim` | dò cả mạng |
| `otl-tim://mo/<ip>` | kiểm một địa chỉ (khách tự nhập, hoặc chọn một trong nhiều máy) rồi mở |
| `otl-tim://lich-su` | mở trang lịch sử lỗi và thao tác |

### lịch sử lỗi và thao tác

Mọi thao tác (cài, tìm máy, mở máy, tạo icon, đưa cửa sổ máy đang mở lên, xem lịch sử) và mọi lỗi (không có
mạng, không thấy máy, panel chưa bật web, IP sai / không trả lời / không phải máy rang, không tạo được icon,
không mở được Edge, không đọc được chứng chỉ, và mọi lỗi chương trình lọt ra ngoài) ghi vào
`%LOCALAPPDATA%\OTL\TimMayRang\lich-su.jsonl`, giữ 2000 mục mới nhất. Link **Xem lịch sử lỗi và thao tác** trên
trang gọi `otl-tim://lich-su`: phần mềm dựng `lich-su.html` từ mẫu `app/lich-su.html` nhúng trong exe rồi mở bằng
trình duyệt. Trang lọc Tất cả / Lỗi / Thao tác và có nút **Lưu tệp gửi OTL** (tệp .txt để gửi qua Zalo).
`nhat-ky.txt` vẫn giữ dòng kỹ thuật chi tiết cho người sửa phần mềm.

**Lưu trên web (bản 1.3).** Mỗi máy tính là một quán: mã + khoá ghi + khoá xem, sinh một lần ở `quan.txt`.
Mục lịch sử mới vào thêm `cho-gui.jsonl`; mỗi lần phần mềm chạy nó gửi lên máy chủ, mất mạng thì để lần sau.
Địa chỉ máy chủ đọc từ [`may-chu.json`](may-chu.json) trên trang (trống = chưa bật, phần mềm mở bản trên máy).

- Khách: **Xem lịch sử lỗi và thao tác của quán** mở `lich-su.html#ma=…&k=…`, link mở ở máy nào cũng xem được.
- OTL: [`quan-ly.html`](https://tcl-da.github.io/tim-may-rang/quan-ly.html), mật khẩu là secret `MAT_KHAU` của Worker;
  danh sách quán (lỗi 7 ngày xếp trước), đặt tên quán, xem lịch sử từng quán.
- Máy chủ: [`may-chu/`](may-chu) — Cloudflare Worker + D1 (`worker.js`, `schema.sql`, `wrangler.toml`). Khoá chỉ lưu
  dạng SHA-256, mỗi quán giữ 5000 mục.

```powershell
cd may-chu
npx wrangler@3 d1 execute otl-tim-may-rang --remote --file schema.sql
npx wrangler@3 deploy                      # in ra địa chỉ https://otl-tim-may-rang.<...>.workers.dev
npx wrangler@3 secret put MAT_KHAU         # mật khẩu trang quản lý OTL
# rồi ghi địa chỉ đó vào may-chu.json và đẩy lên
```

Thử ở máy: `npx wrangler@3 dev --local` (mật khẩu thử trong `may-chu/.dev.vars`), chạy phần mềm với biến môi
trường `OTL_TIM_MAY_CHU=http://127.0.0.1:8787`.

Đúng một máy sẵn sàng thì phần mềm mở luôn màn hình máy, không mở thêm gì. Các trường hợp còn lại nó
mở lại trang với `#ket=…` để trang báo: `nhieu` (kèm `may=`), `chua-bat` (`chua=`), `khong-thay`
(`mang=`), `khong-mang`, `ip-sai` / `khong-tra-loi` / `khong-phai-may` (`ip=`).

Khi dò, phần mềm:

1. Lấy các mạng đang nối (Wi-Fi, mạng dây). Bỏ qua card VPN, ảo, Bluetooth. Mạng rộng hơn /22 thì chỉ
   dò /24 quanh máy tính.
2. Thử cổng 443 trên mọi địa chỉ cùng một lượt (tới 1024 kết nối song song, mỗi kết nối chờ 0,4 s).
   Đã thấy máy thì chỉ chờ thiết bị chậm thêm 0,3 s; lượt nhanh không thấy máy nào thì dò lại với thời
   gian chờ 1,5 s (máy Wi-Fi đang ngủ). Hai dải /24 (508 địa chỉ): khoảng 0,75 s quét, 1 s cả lượt.
3. Nhận dạng màn hình máy rang (Siemens WinCC Unified), thử lần lượt hai đường:
   - `https://<ip>/device/WebRH`: panel Unified Comfort (MTP…). `/WebRH` của panel luôn trả 503 của
     Industrial Edge, không phải trang runtime;
   - `https://<ip>/WebRH`: Unified PC Runtime.

   Đường nào trả trang `WinCC Unified RT` thì máy **sẵn sàng**. Không đường nào ra mà cổng S7 102 và
   VNC 5900 đều mở thì panel chưa bật Web client, trang báo khách gọi kỹ thuật.
4. Có đúng một máy sẵn sàng thì mở trang đó và tạo icon `Máy rang OTL` ngoài desktop. Có
   nhiều máy thì mỗi máy một icon `Máy rang OTL (<ip>)` và khách chọn máy cần mở.

Màn hình máy mở bằng Edge (không có thì Chrome) chế độ **kiosk**: phủ kín màn hình, không thanh địa chỉ,
không tab, đóng bằng Alt+F4. Không dùng `--app --start-fullscreen` vì hồ sơ đã nhớ khung cửa sổ lần trước
thì Edge bỏ qua `--start-fullscreen`. Mỗi máy một hồ sơ riêng `%LOCALAPPDATA%\OTL\TimMayRang\trinh-duyet\<ip>`;
phần mềm ghim đúng chứng chỉ tự ký của panel (`--ignore-certificate-errors-spki-list`, SHA-256 của khoá
công khai) nên khách không gặp trang "kết nối không riêng tư", và thêm `--test-type` để Edge không hiện
thanh cảnh báo vì cờ đó. Màn hình máy đang mở thì bấm lại chỉ đưa cửa sổ đó lên, không mở cửa sổ thứ hai.

Icon desktop `Máy rang OTL` là `.lnk` gọi phần mềm đã cài với `otl-tim://mo/<ip>`.

Không thấy máy thì khách nhập IP ghi trên màn hình máy. Phần mềm chỉ nhận IP mạng nội bộ và chỉ mở
địa chỉ đã nhận dạng là màn hình máy rang. Hai lần bấm dồn thì lần sau bỏ qua (mutex).

Nhật ký ghi ở `%LOCALAPPDATA%\OTL\TimMayRang\nhat-ky.txt`.

---

## máy rang cần gì

Project WinCC Unified của panel phải bật **Web client** và có tài khoản đăng nhập web cho khách.
Muốn màn hình giãn kín cửa sổ trình duyệt, màn hình gốc chạy `HMIRuntime.UI.RootWindow.Adaption = 2;`
(ScreenToWindow, giữ tỉ lệ) khi nạp. Trên panel 1280×800 hệ số là 1 nên panel không đổi. Đã thử
với panel MTP1200 trên máy OTL-30 (02/11/2026): dò ra, mở `https://<ip>/device/WebRH`.

---

## build

```powershell
powershell -ExecutionPolicy Bypass -File app\build.ps1
```

Dùng `csc` có sẵn trong .NET Framework 4.x (mã C# 5), rồi đóng gói `OTL-Tim-May-Rang.zip` ở gốc repo
(tên không mang số bản để link tải không phải đổi).

Thử mà không cài, không mở trình duyệt, không ghi lên desktop thật (kết quả xem trong nhật ký):

```powershell
& "app\build\OTL-Tim-May-Rang\OTL Tim May Rang.exe" --thu C:\tam\icon otl-tim://tim          # dò cả máy này
& "app\build\OTL-Tim-May-Rang\OTL Tim May Rang.exe" --thu C:\tam\icon otl-tim://mo/192.168.0.20
& "app\build\OTL-Tim-May-Rang\OTL Tim May Rang.exe" --do 192.168.0.0/24                      # chỉ dò
```

| Tệp | Việc |
|---|---|
| `index.html` | Trang web (một tệp, không thư viện ngoài, chỉ tải font Be Vietnam Pro) |
| `app/TimMayRang.cs` | Tự cài + `otl-tim://`, dò mạng, nhận dạng panel, mở toàn màn hình, tạo icon |
| `app/build.ps1` | Build exe + đóng gói zip |
| `tai-nguyen/` | Logo, ảnh máy, icon của trang |
