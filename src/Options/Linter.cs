using System.ComponentModel;
using Community.VisualStudio.Toolkit;
using TailwindCSSIntellisense.Linting;

namespace TailwindCSSIntellisense.Options;

public class Linter : BaseOptionModel<Linter>
{
    [Category("General")]
    [DisplayName("Enable linter")]
    [Description("Enables or disables the entire linter.")]
    [DefaultValue(true)]
    public bool Enabled { get; set; } = true;

    [Category("Validation")]
    [DisplayName("Invalid screen")]
    [Description("Unknown screen name used with the @screen directive.")]
    [TypeConverter(typeof(EnumConverter))]
    [DefaultValue(ErrorSeverity.Error)]
    public ErrorSeverity InvalidScreen { get; set; } = ErrorSeverity.Error;

    [Category("Validation")]
    [DisplayName("Invalid Tailwind directive")]
    [Description("Unknown value used with the @tailwind directive.")]
    [TypeConverter(typeof(EnumConverter))]
    [DefaultValue(ErrorSeverity.Error)]
    public ErrorSeverity InvalidTailwindDirective { get; set; } = ErrorSeverity.Error;

    [Category("Validation")]
    [DisplayName("Invalid config path")]
    [Description("Unknown or invalid path used with the theme helper.")]
    [TypeConverter(typeof(EnumConverter))]
    [DefaultValue(ErrorSeverity.Error)]
    public ErrorSeverity InvalidConfigPath { get; set; } = ErrorSeverity.Error;

    [Category("Validation")]
    [DisplayName("CSS conflict")]
    [Description(
        "Class names on the same HTML element / CSS class which apply the same CSS property or properties."
    )]
    [TypeConverter(typeof(EnumConverter))]
    [DefaultValue(ErrorSeverity.Warning)]
    public ErrorSeverity CssConflict { get; set; } = ErrorSeverity.Warning;

    [Category("Validation")]
    [DisplayName("Deprecated at-rule")]
    [Description("Deprecated at-rule used in CSS, such as @variant instead of @custom-variant.")]
    [TypeConverter(typeof(EnumConverter))]
    [DefaultValue(ErrorSeverity.Warning)]
    public ErrorSeverity DeprecatedAtRule { get; set; } = ErrorSeverity.Warning;

    [Category("Validation")]
    [DisplayName("Used blocklist class")]
    [Description("A Tailwind class that has been added to the configured blocklist is used.")]
    [TypeConverter(typeof(EnumConverter))]
    [DefaultValue(ErrorSeverity.Warning)]
    public ErrorSeverity UsedBlocklistClass { get; set; } = ErrorSeverity.Warning;

    [Category("Validation")]
    [DisplayName("Invalid source")]
    [Description(
        "Invalid or missing path provided to a source() argument in @source, @import, or @tailwind directives."
    )]
    [TypeConverter(typeof(EnumConverter))]
    [DefaultValue(ErrorSeverity.Error)]
    public ErrorSeverity InvalidSource { get; set; } = ErrorSeverity.Error;
}
