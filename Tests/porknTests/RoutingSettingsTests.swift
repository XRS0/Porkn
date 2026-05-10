import XCTest

@testable import porkn

final class RoutingSettingsTests: XCTestCase {
  func testParsesDirectDomainsAsSuffixRules() {
    let parsed = DomainRuleParser.parse("*.ru, .su\nx.com https://twitter.com/path example.org:443")

    XCTAssertEqual(parsed.domainSuffix, ["ru", "su", "x.com", "twitter.com", "example.org"])
    XCTAssertEqual(parsed.domain, [])
  }

  func testGeneratorAddsDirectDomainRule() throws {
    let profile = try ConfigParser().parseOne(
      "vless://uuid@example.com:443?security=reality&type=tcp&sni=example.com&fp=chrome&pbk=public&sid=short#VLESS"
    )
    let settings = RoutingSettings(directDomainsText: "*.ru\nx.com")
    let json = try SingBoxConfigGenerator().generate(
      profile: profile, mode: .localProxy, routingSettings: settings)

    XCTAssertTrue(json.contains("\"domain_suffix\" : ["))
    XCTAssertTrue(json.contains("\"ru\""))
    XCTAssertTrue(json.contains("\"x.com\""))
    XCTAssertTrue(json.contains("\"outbound\" : \"direct\""))
  }

  func testRoutingSettingsExportImportRoundTrip() throws {
    let settings = RoutingSettings(
      preset: .custom,
      directDomainsText: "*.ru",
      proxyDomainsText: "chatgpt.com",
      blockDomainsText: "ads.example.com"
    )

    let json = try settings.exportJSONString()
    let imported = try RoutingSettings.importJSONString(json)

    XCTAssertEqual(imported, settings)
  }

  func testGeneratorAddsProxyAndBlockDomainRules() throws {
    let profile = try ConfigParser().parseOne(
      "vless://uuid@example.com:443?security=reality&type=tcp&sni=example.com&fp=chrome&pbk=public&sid=short#VLESS"
    )
    let settings = RoutingSettings(
      preset: .custom,
      directDomainsText: "*.ru",
      proxyDomainsText: "chatgpt.com",
      blockDomainsText: "ads.example.com"
    )
    let json = try SingBoxConfigGenerator().generate(
      profile: profile, mode: .localProxy, routingSettings: settings)

    XCTAssertTrue(json.contains("\"outbound\" : \"block\""))
    XCTAssertTrue(json.contains("ads.example.com"))
    XCTAssertTrue(json.contains("chatgpt.com"))
    XCTAssertTrue(json.contains("\"outbound\" : \"proxy-out\""))
  }

}
