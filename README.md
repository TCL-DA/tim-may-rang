# Tìm máy rang OTL

Phần mềm nhỏ cho khách của OTL: bấm **Tìm máy rang**, phần mềm tự dò máy rang trong mạng của quán, mở
màn hình điều khiển của máy lên trình duyệt, rồi tạo icon **Máy rang OTL** ngoài desktop. Lần sau khách
chỉ cần bấm đúp icon đó.

Tải: [`OTL-Tim-May-Rang-v1.0.zip`](OTL-Tim-May-Rang-v1.0.zip), Windows 10/11, không cần cài đặt.

## Cách chạy

`OTL Tim May Rang.exe` dựng một máy chủ web nhỏ chỉ nghe `127.0.0.1:47830` rồi mở `index.html` bằng
trình duyệt mặc định. Bấm nút, phần mềm sẽ:

1. Lấy các mạng đang nối (Wi-Fi, mạng dây). Bỏ qua card VPN, ảo, Bluetooth. Mạng rộng hơn /22 thì chỉ
   dò /24 quanh máy tính.
2. Thử cổng 443 trên mọi địa chỉ (192 kết nối song song, mỗi kết nối chờ 0,7 s). Một dải /24 xong
   trong khoảng 1,5 giây.
3. Nhận dạng màn hình máy rang (Siemens WinCC Unified), thử lần lượt hai đường:
   - `https://<ip>/device/WebRH`: panel Unified Comfort (MTP…). `/WebRH` của panel luôn trả 503 của
     Industrial Edge, không phải trang runtime;
   - `https://<ip>/WebRH`: Unified PC Runtime.

   Đường nào trả trang `WinCC Unified RT` thì máy **sẵn sàng**. Không đường nào ra mà cổng S7 102 và
   VNC 5900 đều mở thì panel chưa bật Web client, trang báo khách gọi kỹ thuật.
4. Có đúng một máy sẵn sàng thì mở trang đó và tạo icon `Máy rang OTL` ngoài desktop. Có
   nhiều máy thì mỗi máy một icon `Máy rang OTL (<ip>)` và khách chọn máy cần mở.

Màn hình máy mở bằng Edge (không có thì Chrome) ở chế độ ứng dụng toàn màn hình: không thanh địa chỉ,
không tab; F11 thu về cửa sổ, Alt+F4 đóng. Trình duyệt chạy hồ sơ riêng
`%LOCALAPPDATA%\OTL\TimMayRang	rinh-duyet` và ghim đúng chứng chỉ tự ký của panel
(`--ignore-certificate-errors-spki-list`, SHA-256 của khoá công khai), nên khách không gặp trang
"kết nối không riêng tư". Icon desktop là `.lnk` mang đúng các cờ đó. Máy không có Edge/Chrome thì
quay về `.url` mở bằng trình duyệt mặc định.

Không thấy máy thì khách nhập IP ghi trên màn hình máy. Phần mềm chỉ nhận IP mạng nội bộ và chỉ mở
địa chỉ đã nhận dạng là màn hình máy rang.

Đóng trang 20 giây thì phần mềm tự tắt. Nhật ký ghi ở `%LOCALAPPDATA%\OTL\TimMayRang\nhat-ky.txt`.

Cùng một `index.html` chạy được ở hai chỗ: trong phần mềm thì hiện nút **Tìm máy rang**, còn mở trên
GitHub Pages (trình duyệt không được quét mạng) thì hiện nút **Tải phần mềm**.

## Máy rang cần gì

Project WinCC Unified của panel phải bật **Web client** và có tài khoản đăng nhập web cho khách.
Muốn màn hình giãn kín cửa sổ trình duyệt, màn hình gốc chạy `HMIRuntime.UI.RootWindow.Adaption = 2;`
(ScreenToWindow, giữ tỉ lệ) khi nạp. Trên panel 1280×800 hệ số là 1 nên panel không đổi. Đã thử
với MTP1200 OTL-30 (192.168.100.245, 02/11/2026): dò ra, mở `https://<ip>/device/WebRH`.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File app\build.ps1
```

Dùng `csc` có sẵn trong .NET Framework 4.x (mã C# 5), nhúng `index.html` và `tai-nguyen/` vào exe,
rồi đóng gói `OTL-Tim-May-Rang-v1.0.zip` ở gốc repo.

Thử mà không mở trình duyệt, không ghi lên desktop thật:

```powershell
& "app\build\OTL-Tim-May-Rang-v1.0\OTL Tim May Rang.exe" --thu C:\tam\icon   # icon ghi vào thư mục này
& "app\build\OTL-Tim-May-Rang-v1.0\OTL Tim May Rang.exe" --do 192.168.0.0/24  # chỉ dò, kết quả vào nhật ký
```

| Tệp | Việc |
|---|---|
| `index.html` | Giao diện (một tệp, không thư viện ngoài, chỉ tải font Be Vietnam Pro) |
| `app/TimMayRang.cs` | Máy chủ localhost, dò mạng, nhận dạng panel, tạo icon |
| `app/build.ps1` | Build exe + đóng gói zip |
| `tai-nguyen/` | Logo, ảnh máy, icon dùng chung cho trang và exe |
