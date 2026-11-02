# Thêm hoặc sửa một máy

Chỉ sửa đúng một tệp: `du-lieu/may-rang.json`.

## Cách nhanh nhất (sửa thẳng trên GitHub)

1. Mở https://github.com/TCL-DA/tim-may-rang/blob/main/du-lieu/may-rang.json
2. Bấm biểu tượng bút chì (Edit this file).
3. Thêm một dòng vào mảng `"may"` — **nhớ dấu phẩy ở cuối dòng trước**.
4. Bấm **Commit changes** (ghi thẳng vào `main`).
5. Khoảng 1 phút sau trang web tự cập nhật.

## Mẫu một dòng

```json
{ "hang": "Giesen", "model": "W6A", "nuoc": "Hà Lan", "kg": 6, "kieu": "trống", "nhiet": "gas", "tu_dong": "bán tự động", "nguon": "https://giesencoffeeroasters.com", "da_kiem": true }
```

## Các trường

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `hang` | chữ | Tên hãng, viết nhất quán với các dòng đã có |
| `model` | chữ | Tên model đúng như hãng ghi |
| `nuoc` | chữ | Nước sản xuất, tiếng Việt có dấu |
| `kg` | **số** | Khối lượng mẻ, kg. Không bỏ trong ngoặc kép. Dải thì lấy mức danh định |
| `kieu` | chữ | `trống` · `khí nóng` · `lai` |
| `nhiet` | chữ | `gas` · `điện` |
| `tu_dong` | chữ | `thủ công` · `bán tự động` · `tự động` |
| `nguon` | chữ | Link trang hãng; chưa có thì để `""` |
| `da_kiem` | `true`/`false` | `true` khi đã mở trang/catalog hãng và xác nhận thông số |

Giá trị mới trong `kieu` / `nhiet` / `tu_dong` / `nuoc` tự xuất hiện trong bộ lọc,
nên **đừng gõ lệch chính tả** — lệch một dấu là thành một mục lọc riêng.

## Kiểm trước khi commit

```bash
python -c "import json;d=json.load(open('du-lieu/may-rang.json',encoding='utf-8'));print(len(d['may']),'máy')"
```

JSON sai dấu phẩy thì trang web không hiện gì — chỉ còn dòng báo "Không đọc được dữ liệu".

## Đừng ghi vào repo này

Giá bán, tên khách, thông tin nội bộ của công ty. Repo công khai.
