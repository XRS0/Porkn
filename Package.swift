// swift-tools-version: 6.2
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
            path: "Sources/porkn",
            resources: [.copy("Resources")]
        ),
        .testTarget(
            name: "porknTests",
            dependencies: ["porkn"],
            path: "Tests/porknTests"
        )
    ]
)
