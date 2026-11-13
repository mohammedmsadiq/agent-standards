---
name: review-maui-page
description: Review a .NET MAUI page or view (XAML, code-behind and view model) for binding, layout, performance, accessibility and platform issues. Use when creating or reviewing MAUI UI.
---
# Review a MAUI page

1. **Bindings:** `x:DataType` set for compiled bindings; binding paths exist; commands and properties notify
   correctly.
2. **Layout:** shallow layout hierarchy; `Grid` over nested stacks; `CollectionView` with lightweight templates.
3. **Separation:** logic in the view model or services following the project's MVVM approach; code-behind only for
   purely visual concerns.
4. **Threading:** no blocking work on the UI thread; UI updates on the main thread.
5. **Resources and theming:** shared styles, colours and sizes; light/dark support where the app has it; images
   sized appropriately.
6. **Accessibility:** semantic descriptions, hints and heading levels; touch target sizes; text scaling.
7. **Platform behaviour:** safe areas, keyboard handling, back navigation, permissions.
8. **Report** findings by severity with concrete fixes.
