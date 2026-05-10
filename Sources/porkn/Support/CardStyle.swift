import SwiftUI

struct CardBackground: ViewModifier {
  var material: Material = .regularMaterial
  var radius: CGFloat = 20
  var padding: CGFloat = 20

  func body(content: Content) -> some View {
    content
      .padding(padding)
      .frame(maxWidth: .infinity, alignment: .leading)
      .background(material, in: RoundedRectangle(cornerRadius: radius, style: .continuous))
  }
}

extension View {
  func myTunCard(material: Material = .regularMaterial, radius: CGFloat = 20, padding: CGFloat = 20)
    -> some View
  {
    modifier(CardBackground(material: material, radius: radius, padding: padding))
  }
}
