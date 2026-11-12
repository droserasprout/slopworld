// CUR stores the hotspot in physical image pixels. Keeping it inside the image
// avoids rounding a CSS hotspot before image-set applies a fractional density.
export function cursorImage(png,width,height,x,y) {
  const pixels=Uint8Array.from(atob(png.slice(png.indexOf(',')+1)),c=>c.charCodeAt(0));
  const bytes=new Uint8Array(22+pixels.length),header=new DataView(bytes.buffer);
  header.setUint16(2,2,true);header.setUint16(4,1,true);
  bytes[6]=width===256?0:width;bytes[7]=height===256?0:height;
  header.setUint16(10,Math.max(0,Math.min(width-1,Math.round(x))),true);
  header.setUint16(12,Math.max(0,Math.min(height-1,Math.round(y))),true);
  header.setUint32(14,pixels.length,true);header.setUint32(18,22,true);
  bytes.set(pixels,22);
  return 'data:image/x-icon;base64,'+btoa(Array.from(bytes,byte=>String.fromCharCode(byte)).join(''));
}
