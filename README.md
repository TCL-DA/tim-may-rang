# Tìm máy rang cà phê

Trang web tra cứu máy rang cà phê: lọc theo khối lượng mẻ, kiểu truyền nhiệt, nguồn nhiệt,
mức tự động và nước sản xuất.

**Xem trang:** https://tcl-da.github.io/tim-may-rang/

## Cấu trúc

| Tệp | Việc |
|---|---|
| `index.html` | Toàn bộ trang web (một tệp, không cần build, không thư viện ngoài) |
| `du-lieu/may-rang.json` | Dữ liệu máy — **chỗ duy nhất cần sửa khi thêm máy** |
| `THEM-MAY.md` | Cách thêm/sửa một máy |

## Chạy thử ở máy

Mở `index.html` bằng `file://` sẽ bị chặn `fetch`, nên chạy một server tĩnh:

```bash
python -m http.server 8000
# rồi mở http://localhost:8000
```

## Dữ liệu

Thông số trong `du-lieu/may-rang.json` là bản gieo mầm ban đầu, lấy theo thông tin công khai
của từng hãng và **chưa đối chiếu catalog** — nên mọi máy đang mang thẻ `chưa kiểm`.
Khi đã mở trang của hãng và xác nhận đúng thì đổi `"da_kiem": true`, thẻ đó sẽ tự mất.

Không ghi giá bán, tên khách hay thông tin nội bộ vào repo này — repo công khai.
