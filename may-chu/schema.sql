-- Lịch sử lỗi + thao tác của phần mềm Tìm máy rang OTL.
-- Áp dụng: npx wrangler@3 d1 execute otl-tim-may-rang --remote --file schema.sql

-- Mỗi máy tính cài phần mềm là một "quán". Khoá chỉ lưu dạng SHA-256.
CREATE TABLE IF NOT EXISTS quan (
  ma        TEXT PRIMARY KEY,      -- mã quán phần mềm tự sinh
  bam_ghi   TEXT NOT NULL,         -- SHA-256 khoá ghi (chỉ phần mềm biết)
  bam_xem   TEXT NOT NULL,         -- SHA-256 khoá xem (nằm trong link lịch sử của khách)
  ten       TEXT,                  -- OTL đặt tên ở trang quản lý
  may       TEXT,                  -- tên máy tính
  ban       TEXT,                  -- bản phần mềm
  lan_dau   TEXT NOT NULL,
  lan_cuoi  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS muc (
  id        INTEGER PRIMARY KEY AUTOINCREMENT,
  ma_quan   TEXT NOT NULL,
  t         TEXT NOT NULL,         -- giờ trên máy khách, yyyy-MM-ddTHH:mm:ss
  loai      TEXT NOT NULL,         -- 'loi' | 'thao-tac'
  viec      TEXT NOT NULL,
  chi_tiet  TEXT,
  ms        INTEGER
);
CREATE INDEX IF NOT EXISTS muc_quan ON muc (ma_quan, id);
