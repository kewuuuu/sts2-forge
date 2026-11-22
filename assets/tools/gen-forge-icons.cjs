
// 纯 Node（无第三方依赖）的 PNG 编码器 + 锻铸图标的程序化绘制。
// 用法：node assets/tools/gen-forge-icons.cjs
// 产出：assets/icons/forge_rest_site.png (128x128) 与 assets/icons/forge_rest_site_big.png (512x512)。
// 图形：铁砧 + 锤子，暖金色主体 + 近黑描边，透明背景（火堆按钮会自己加底）。
const fs = require("fs");
const path = require("path");
const zlib = require("zlib");

function crc32(buf) {
  let c, crc = 0xffffffff;
  for (let n = 0; n < buf.length; n++) {
    c = (crc ^ buf[n]) & 0xff;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    crc = (crc >>> 8) ^ c;
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}
function encodePNG(w, h, rgba) {
  const raw = Buffer.alloc((w * 4 + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (w * 4 + 1)] = 0;
    rgba.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", zlib.deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

function canvas(size) { return { size, px: new Float32Array(size * size * 4) }; }

// 4x4 超采样填充：test(x,y) 用 128 空间的坐标判断；alpha 渐变让边缘不锯齿。
function paint(cv, test, color, alpha = 1) {
  const { size, px } = cv;
  const SS = 4, inv = 1 / (SS * SS);
  const [r, g, b] = color;
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      let hit = 0;
      for (let sy = 0; sy < SS; sy++) for (let sx = 0; sx < SS; sx++) {
        if (test(x + (sx + 0.5) / SS, y + (sy + 0.5) / SS)) hit++;
      }
      if (!hit) continue;
      const a = (hit * inv) * alpha;
      const i = (y * size + x) * 4;
      const da = px[i + 3] / 255;
      const outA = a + da * (1 - a);
      for (let c = 0; c < 3; c++) {
        px[i + c] = outA === 0 ? 0 : (color[c] * a + px[i + c] * da * (1 - a)) / outA;
      }
      // 通道统一用 0..255：颜色直接是 0..255，alpha 也要乘回去。
      px[i + 3] = outA * 255;
    }
  }
}

// 形状（128 空间的坐标）
const rrect = (cx, cy, w, h, rad) => (x, y) => {
  const hw = w / 2, hh = h / 2, dx = Math.abs(x - cx), dy = Math.abs(y - cy);
  if (dx > hw || dy > hh) return false;
  const qx = Math.max(dx - (hw - rad), 0), qy = Math.max(dy - (hh - rad), 0);
  return qx * qx + qy * qy <= rad * rad;
};
const disc = (cx, cy, r) => (x, y) => Math.hypot(x - cx, y - cy) <= r;
const ellipse = (cx, cy, rx, ry) => (x, y) => {
  const dx = (x - cx) / rx, dy = (y - cy) / ry;
  return dx * dx + dy * dy <= 1;
};
const bar = (x0, y0, x1, y1, half) => (x, y) => {
  const dx = x1 - x0, dy = y1 - y0;
  const len2 = dx * dx + dy * dy;
  const t = Math.max(0, Math.min(1, ((x - x0) * dx + (y - y0) * dy) / len2));
  const px = x0 + dx * t, py = y0 + dy * t;
  return Math.hypot(x - px, y - py) <= half;
};
const union = (...fns) => (x, y) => fns.some((f) => f(x, y));
const minus = (a, b) => (x, y) => a(x, y) && !b(x, y);

// 铁砧：上宽下窄的砧身 + 底座；锤子斜放在砧面上。
function anvilBody() {
  const top = rrect(64, 74, 74, 15, 5);        // 砧面
  const waist = rrect(64, 90, 26, 18, 6);      // 砧腰
  const foot = rrect(64, 103, 48, 11, 4);      // 底座
  return union(top, waist, foot);
}
function hammerShape() {
  const handle = bar(88, 34, 62, 62, 4.5);     // 锤柄
  const head = rrect(94, 30, 26, 15, 4);       // 锤头
  return union(handle, head);
}

function draw(size) {
  const cv = canvas(size);
  const s = size / 128;
  const shape = (fn) => (x, y) => fn(x / s, y / s);
  const anvil = anvilBody();
  const hammer = hammerShape();
  const spark = (cx, cy, r) => (x, y) => {
    const dx = Math.abs(x - cx), dy = Math.abs(y - cy);
    return (dx * dx + dy * dy <= r * r) || (dx <= r * 0.28 && dy <= r) || (dy <= r * 0.28 && dx <= r);
  };

  // 1) 深色描边：所有形状向外扩 3px（用粗一层的同形近似）
  paint(cv, shape((x, y) => anvil(x, y) || hammer(x, y)), [26, 20, 16], 1);
  const outline = union(
    rrect(64, 74, 80, 21, 6), rrect(64, 90, 32, 24, 8), rrect(64, 103, 54, 17, 6),
    bar(88, 34, 62, 62, 8.5), rrect(94, 30, 32, 21, 6)
  );
  paint(cv, shape(outline), [26, 20, 16], 1);

  // 2) 砧身：暗钢灰，上沿一道亮边
  paint(cv, shape(anvil), [92, 96, 104], 1);
  paint(cv, shape(rrect(64, 68, 74, 4, 2)), [150, 156, 166], 1);
  paint(cv, shape(minus(rrect(64, 74, 74, 15, 5), rrect(64, 75, 74, 11, 5))), [122, 128, 140], 1);

  // 3) 锤子：木柄 + 金色锤头
  paint(cv, shape(bar(88, 34, 62, 62, 4.5)), [150, 104, 56], 1);
  paint(cv, shape(rrect(94, 30, 26, 15, 4)), [232, 178, 74], 1);
  paint(cv, shape(rrect(94, 26, 26, 6, 3)), [255, 214, 128], 1);

  // 4) 火星：锻打点上的三颗亮点
  paint(cv, shape(spark(58, 48, 4.2)), [255, 236, 170], 1);
  paint(cv, shape(spark(68, 40, 3.2)), [255, 214, 128], 1);
  paint(cv, shape(spark(50, 58, 2.6)), [255, 196, 96], 1);
  return cv;
}

function write(file, size) {
  const cv = draw(size);
  const rgba = Buffer.alloc(size * size * 4);
  // px 里存的就是 0..255 的通道值（paint 直接收 0..255），这里只做取整与夹取。
  for (let i = 0; i < cv.px.length; i++) rgba[i] = Math.max(0, Math.min(255, Math.round(cv.px[i])));
  fs.writeFileSync(file, encodePNG(size, size, rgba));
  return fs.statSync(file).size;
}

const dir = path.resolve(__dirname, "..", "icons");
fs.mkdirSync(dir, { recursive: true });
const a = write(path.join(dir, "forge_rest_site.png"), 128);
const b = write(path.join(dir, "forge_rest_site_big.png"), 512);
console.log("forge_rest_site.png " + a + " bytes");
console.log("forge_rest_site_big.png " + b + " bytes");
