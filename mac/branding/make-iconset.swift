// Renders branding/logo.svg into a macOS iconset (transparent corners). Regenerate DumpFinder.icns with:
//   mkdir -p /tmp/DumpFinder.iconset && swift mac/branding/make-iconset.swift branding/logo.svg /tmp/DumpFinder.iconset
//   iconutil -c icns /tmp/DumpFinder.iconset -o mac/branding/DumpFinder.icns
import AppKit
let args = CommandLine.arguments
guard let img = NSImage(contentsOf: URL(fileURLWithPath: args[1])) else { fatalError("cannot load svg") }
let outDir = args[2]
for (name, px) in [("16x16",16),("16x16@2x",32),("32x32",32),("32x32@2x",64),("128x128",128),("128x128@2x",256),("256x256",256),("256x256@2x",512),("512x512",512),("512x512@2x",1024)] {
  let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
  NSGraphicsContext.saveGraphicsState()
  NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
  NSColor.clear.set(); NSRect(x: 0, y: 0, width: px, height: px).fill()
  img.draw(in: NSRect(x: 0, y: 0, width: px, height: px))
  NSGraphicsContext.restoreGraphicsState()
  try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: "\(outDir)/icon_\(name).png"))
}
