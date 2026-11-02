// Máy chủ lịch sử của phần mềm Tìm máy rang OTL.
//
//   POST /ghi            phần mềm gửi các mục lịch sử của một quán (khoá ghi)
//   GET  /quan?ma=…      lịch sử một quán — khách gửi khoá xem (x-khoa), OTL gửi mật khẩu (x-mk)
//   GET  /ds             danh sách quán (x-mk)
//   POST /dat-ten        OTL đặt tên quán (x-mk)
//
// Khoá đi trong header chứ không trong URL để khỏi nằm trong nhật ký truy cập. Không có cookie nên CORS mở.

const GIU_MUC = 5000;       // mỗi quán giữ 5000 mục mới nhất
const TOI_DA_GUI = 500;     // một lần gửi tối đa

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
  "Access-Control-Allow-Headers": "content-type,x-khoa,x-mk",
};

function json(o, trangThai = 200) {
  return new Response(JSON.stringify(o), {
    status: trangThai,
    headers: { ...CORS, "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
  });
}

async function bam(s) {
  const b = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(s));
  return [...new Uint8Array(b)].map((x) => x.toString(16).padStart(2, "0")).join("");
}

// So hai chuỗi không lộ độ dài khớp qua thời gian.
function bang(a, b) {
  if (typeof a !== "string" || typeof b !== "string" || a.length !== b.length) return false;
  let k = 0;
  for (let i = 0; i < a.length; i++) k |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return k === 0;
}

const laOTL = (req, env) => !!env.MAT_KHAU && bang(req.headers.get("x-mk") || "", env.MAT_KHAU);
const cat = (s, n) => (s == null ? null : String(s).slice(0, n));
const laMa = (s) => typeof s === "string" && /^[A-Z0-9]{6,16}$/.test(s);
const laKhoa = (s) => typeof s === "string" && s.length >= 16 && s.length <= 64;

async function ghi(req, env) {
  const d = await req.json().catch(() => null);
  if (!d || !laMa(d.ma) || !laKhoa(d.khoaGhi) || !Array.isArray(d.muc)) return json({ loi: "sai-du-lieu" }, 400);
  if (d.muc.length > TOI_DA_GUI) return json({ loi: "qua-nhieu" }, 413);
  const bay = new Date().toISOString().slice(0, 19);
  const q = await env.DB.prepare("SELECT bam_ghi FROM quan WHERE ma = ?").bind(d.ma).first();
  if (q) {
    if (!bang(await bam(d.khoaGhi), q.bam_ghi)) return json({ loi: "sai-khoa" }, 403);
  } else {
    if (!laKhoa(d.khoaXem)) return json({ loi: "sai-du-lieu" }, 400);
    await env.DB.prepare("INSERT INTO quan (ma, bam_ghi, bam_xem, lan_dau, lan_cuoi) VALUES (?, ?, ?, ?, ?)")
      .bind(d.ma, await bam(d.khoaGhi), await bam(d.khoaXem), bay, bay).run();
  }
  const lenh = [
    env.DB.prepare("UPDATE quan SET may = ?, ban = ?, lan_cuoi = ? WHERE ma = ?").bind(cat(d.may, 100), cat(d.ban, 20), bay, d.ma),
  ];
  for (const m of d.muc) {
    if (!m || typeof m.t !== "string" || (m.loai !== "loi" && m.loai !== "thao-tac")) continue;
    lenh.push(env.DB.prepare("INSERT INTO muc (ma_quan, t, loai, viec, chi_tiet, ms) VALUES (?, ?, ?, ?, ?, ?)")
      .bind(d.ma, cat(m.t, 19), m.loai, cat(m.viec, 200) || "", cat(m.chiTiet, 2000), Number.isFinite(m.ms) ? Math.round(m.ms) : null));
  }
  lenh.push(env.DB.prepare("DELETE FROM muc WHERE ma_quan = ?1 AND id <= (SELECT id FROM muc WHERE ma_quan = ?1 ORDER BY id DESC LIMIT 1 OFFSET ?2)")
    .bind(d.ma, GIU_MUC));
  await env.DB.batch(lenh);
  return json({ ok: true, nhan: lenh.length - 2 });
}

async function quan(req, env, url) {
  const ma = url.searchParams.get("ma");
  if (!laMa(ma)) return json({ loi: "sai-ma" }, 400);
  const q = await env.DB.prepare("SELECT ma, bam_xem, ten, may, ban, lan_dau, lan_cuoi FROM quan WHERE ma = ?").bind(ma).first();
  if (!q) return json({ loi: "khong-co-quan" }, 404);
  const duoc = laOTL(req, env) || bang(await bam(req.headers.get("x-khoa") || ""), q.bam_xem);
  if (!duoc) return json({ loi: "sai-khoa" }, 403);
  const r = await env.DB.prepare("SELECT t, loai, viec, chi_tiet AS chiTiet, ms FROM muc WHERE ma_quan = ? ORDER BY id DESC LIMIT 2000").bind(ma).all();
  delete q.bam_xem;
  return json({ quan: q, muc: r.results });
}

async function ds(req, env) {
  if (!laOTL(req, env)) return json({ loi: "sai-mat-khau" }, 403);
  const r = await env.DB.prepare(
    "SELECT q.ma, q.ten, q.may, q.ban, q.lan_dau, q.lan_cuoi," +
    " (SELECT COUNT(*) FROM muc m WHERE m.ma_quan = q.ma AND m.loai = 'loi' AND m.t >= ?) AS loi7," +
    " (SELECT COUNT(*) FROM muc m WHERE m.ma_quan = q.ma) AS tong" +
    " FROM quan q ORDER BY q.lan_cuoi DESC"
  ).bind(new Date(Date.now() - 7 * 864e5).toISOString().slice(0, 10)).all();
  return json({ quan: r.results });
}

async function datTen(req, env) {
  if (!laOTL(req, env)) return json({ loi: "sai-mat-khau" }, 403);
  const d = await req.json().catch(() => null);
  if (!d || !laMa(d.ma)) return json({ loi: "sai-du-lieu" }, 400);
  await env.DB.prepare("UPDATE quan SET ten = ? WHERE ma = ?").bind(cat(d.ten, 100) || null, d.ma).run();
  return json({ ok: true });
}

export default {
  async fetch(req, env) {
    if (req.method === "OPTIONS") return new Response(null, { headers: CORS });
    const url = new URL(req.url);
    try {
      if (url.pathname === "/ghi" && req.method === "POST") return await ghi(req, env);
      if (url.pathname === "/quan" && req.method === "GET") return await quan(req, env, url);
      if (url.pathname === "/ds" && req.method === "GET") return await ds(req, env);
      if (url.pathname === "/dat-ten" && req.method === "POST") return await datTen(req, env);
      return json({ loi: "khong-co" }, 404);
    } catch (e) {
      console.log("loi", e && e.stack);
      return json({ loi: "may-chu" }, 500);
    }
  },
};
