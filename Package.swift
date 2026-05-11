// swift-tools-version: 6.1
import PackageDescription

let package = Package(
    name: "porkn",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "porkn", targets: ["porkn"])
    ],
    dependencies: [],
    targets: [
        .executableTarget(
            name: "porkn",
            path: "apps/macos/Sources/porkn",
            resources: [.copy("Resources")]
        ),
        .testTarget(
            name: "porknTests",
            dependencies: ["porkn"],
            path: "apps/macos/Tests/porknTests"
        )
    ]
)
