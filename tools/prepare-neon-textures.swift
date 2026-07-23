#!/usr/bin/env swift

import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

let projectRoot = URL(fileURLWithPath: FileManager.default.currentDirectoryPath, isDirectory: true)
let inputDirectory = projectRoot
    .appendingPathComponent("assets/source/neon-letters/textures", isDirectory: true)
let outputDirectory = projectRoot
    .appendingPathComponent("assets/processed/neon-letters/textures", isDirectory: true)

try FileManager.default.createDirectory(at: outputDirectory, withIntermediateDirectories: true)

struct TextureConversion {
    let sourceName: String
    let outputName: String
}

func runSipsConversion(from source: URL, to destination: URL) throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/sips")
    process.arguments = ["-s", "format", "png", source.path, "--out", destination.path]
    try process.run()
    process.waitUntilExit()

    guard process.terminationStatus == 0 else {
        throw NSError(domain: "NeonTexturePreparation", code: Int(process.terminationStatus), userInfo: [
            NSLocalizedDescriptionKey: "sips failed for \(source.lastPathComponent)"
        ])
    }
}

func loadImage(from url: URL) throws -> CGImage {
    guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        throw NSError(domain: "NeonTexturePreparation", code: 1, userInfo: [
            NSLocalizedDescriptionKey: "Unable to read image: \(url.path)"
        ])
    }
    return image
}

func writePNG(_ image: CGImage, to url: URL) throws {
    guard let destination = CGImageDestinationCreateWithURL(
        url as CFURL,
        UTType.png.identifier as CFString,
        1,
        nil
    ) else {
        throw NSError(domain: "NeonTexturePreparation", code: 2, userInfo: [
            NSLocalizedDescriptionKey: "Unable to create PNG destination: \(url.path)"
        ])
    }

    CGImageDestinationAddImage(destination, image, nil)
    guard CGImageDestinationFinalize(destination) else {
        throw NSError(domain: "NeonTexturePreparation", code: 3, userInfo: [
            NSLocalizedDescriptionKey: "Unable to write PNG: \(url.path)"
        ])
    }
}

func invertedRGBImage(_ image: CGImage) throws -> CGImage {
    let width = image.width
    let height = image.height
    var pixels = [UInt8](repeating: 0, count: width * height * 4)

    guard let context = CGContext(
        data: &pixels,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: width * 4,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else {
        throw NSError(domain: "NeonTexturePreparation", code: 4, userInfo: [
            NSLocalizedDescriptionKey: "Unable to create image buffer"
        ])
    }

    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

    for index in stride(from: 0, to: pixels.count, by: 4) {
        pixels[index] = 255 - pixels[index]
        pixels[index + 1] = 255 - pixels[index + 1]
        pixels[index + 2] = 255 - pixels[index + 2]
    }

    guard let result = context.makeImage() else {
        throw NSError(domain: "NeonTexturePreparation", code: 5, userInfo: [
            NSLocalizedDescriptionKey: "Unable to create inverted image"
        ])
    }
    return result
}

let conversions = [
    TextureConversion(sourceName: "Neon_letters_albedo.jpeg", outputName: "NeonLetters_Albedo_Source.png"),
    TextureConversion(sourceName: "Neon_letters_emissive.jpeg", outputName: "NeonLetters_Emissive_Source.png"),
    TextureConversion(sourceName: "Neon_letters_metallic.jpeg", outputName: "NeonLetters_Metallic_Source.png"),
    TextureConversion(sourceName: "Neon_letters_normal.jpeg", outputName: "NeonLetters_Normal_Source.png"),
    TextureConversion(sourceName: "Neon_letters_roughness.jpeg", outputName: "NeonLetters_Roughness_Source.png")
]

for conversion in conversions {
    let source = inputDirectory.appendingPathComponent(conversion.sourceName)
    let destination = outputDirectory.appendingPathComponent(conversion.outputName)
    try runSipsConversion(from: source, to: destination)
}

let roughnessSource = inputDirectory.appendingPathComponent("Neon_letters_roughness.jpeg")
let smoothnessImage = try invertedRGBImage(loadImage(from: roughnessSource))
try writePNG(smoothnessImage, to: outputDirectory.appendingPathComponent("NeonLetters_Smoothness.png"))

var whitePixel: [UInt8] = [255, 255, 255, 255]
guard let whiteContext = CGContext(
    data: &whitePixel,
    width: 1,
    height: 1,
    bitsPerComponent: 8,
    bytesPerRow: 4,
    space: CGColorSpaceCreateDeviceRGB(),
    bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
), let whiteImage = whiteContext.makeImage() else {
    throw NSError(domain: "NeonTexturePreparation", code: 6, userInfo: [
        NSLocalizedDescriptionKey: "Unable to create emission mask"
    ])
}
try writePNG(whiteImage, to: outputDirectory.appendingPathComponent("NeonLetters_EmissionMask.png"))

print("Prepared textures in \(outputDirectory.path)")
