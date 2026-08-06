using System.Runtime.CompilerServices;
using System.Windows;

// RemoveSucceededFromInspection のように、確認ダイアログ越しでは自動テストできないロジックを
// internal 経由で単体テストするために公開する。
[assembly: InternalsVisibleTo("MusicTagAuditor.App.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
