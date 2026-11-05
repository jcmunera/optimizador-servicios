// Tema visual (estilo Fluent): colores claro/oscuro según Windows y estilos de los controles.
// Los estilos se escriben en XAML y se cargan en tiempo de ejecución, así el proyecto se
// sigue compilando solo con csc.exe, sin MSBuild.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Markup;
using Microsoft.Win32;

static class Tema
{
    public static bool Oscuro { get; private set; }

    static readonly Dictionary<string, string> Claro = new Dictionary<string, string>
    {
        { "Fondo", "#F3F3F3" }, { "Lateral", "#EBEBEB" }, { "Tarjeta", "#FFFFFF" }, { "Borde", "#E3E3E3" },
        { "BordeFuerte", "#8A8A8A" }, { "Texto", "#1B1B1B" }, { "TextoSecundario", "#5F5F5F" },
        { "Acento", "#005FB8" }, { "AcentoTexto", "#FFFFFF" }, { "Hover", "#E6E6E6" }, { "Seleccion", "#DCDCDC" },
        { "Pulgar", "#B5B5B5" },
        { "VerdeTexto", "#0F7B0F" }, { "VerdeFondo", "#DFF6DD" },
        { "AmarilloTexto", "#8A5300" }, { "AmarilloFondo", "#FFF4CE" },
        { "RojoTexto", "#C42B1C" }, { "RojoFondo", "#FDE7E9" },
        { "AzulTexto", "#005FB8" }, { "AzulFondo", "#E0EEF9" },
        { "GrisTexto", "#5F5F5F" }, { "GrisFondo", "#EBEBEB" },
    };

    static readonly Dictionary<string, string> OscuroColores = new Dictionary<string, string>
    {
        { "Fondo", "#202020" }, { "Lateral", "#1A1A1A" }, { "Tarjeta", "#2B2B2B" }, { "Borde", "#3A3A3A" },
        { "BordeFuerte", "#9A9A9A" }, { "Texto", "#FFFFFF" }, { "TextoSecundario", "#C5C5C5" },
        { "Acento", "#60CDFF" }, { "AcentoTexto", "#002642" }, { "Hover", "#353535" }, { "Seleccion", "#3D3D3D" },
        { "Pulgar", "#6E6E6E" },
        { "VerdeTexto", "#6CCB5F" }, { "VerdeFondo", "#1F3A1C" },
        { "AmarilloTexto", "#FCE100" }, { "AmarilloFondo", "#433519" },
        { "RojoTexto", "#FF99A4" }, { "RojoFondo", "#442726" },
        { "AzulTexto", "#60CDFF" }, { "AzulFondo", "#1E3A4C" },
        { "GrisTexto", "#C5C5C5" }, { "GrisFondo", "#3A3A3A" },
    };

    public static void Aplicar(Application app)
    {
        // OPTIMIZADOR_TEMA=claro|oscuro fuerza un tema (útil para revisar ambos).
        string forzado = System.Environment.GetEnvironmentVariable("OPTIMIZADOR_TEMA");
        Oscuro = forzado == "oscuro" || (forzado != "claro" && WindowsEnModoOscuro());
        string xaml = Estilos;
        foreach (var kv in Oscuro ? OscuroColores : Claro)
            xaml = xaml.Replace("@" + kv.Key + "@", kv.Value);
        app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
    }

    static bool WindowsEnModoOscuro()
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                return k != null && System.Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch { return false; }
    }

    const string Estilos = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='Fondo' Color='@Fondo@'/>
  <SolidColorBrush x:Key='Lateral' Color='@Lateral@'/>
  <SolidColorBrush x:Key='Tarjeta' Color='@Tarjeta@'/>
  <SolidColorBrush x:Key='Borde' Color='@Borde@'/>
  <SolidColorBrush x:Key='BordeFuerte' Color='@BordeFuerte@'/>
  <SolidColorBrush x:Key='Texto' Color='@Texto@'/>
  <SolidColorBrush x:Key='TextoSecundario' Color='@TextoSecundario@'/>
  <SolidColorBrush x:Key='Acento' Color='@Acento@'/>
  <SolidColorBrush x:Key='AcentoTexto' Color='@AcentoTexto@'/>
  <SolidColorBrush x:Key='Hover' Color='@Hover@'/>
  <SolidColorBrush x:Key='Seleccion' Color='@Seleccion@'/>
  <SolidColorBrush x:Key='Pulgar' Color='@Pulgar@'/>
  <SolidColorBrush x:Key='VerdeTexto' Color='@VerdeTexto@'/>
  <SolidColorBrush x:Key='VerdeFondo' Color='@VerdeFondo@'/>
  <SolidColorBrush x:Key='AmarilloTexto' Color='@AmarilloTexto@'/>
  <SolidColorBrush x:Key='AmarilloFondo' Color='@AmarilloFondo@'/>
  <SolidColorBrush x:Key='RojoTexto' Color='@RojoTexto@'/>
  <SolidColorBrush x:Key='RojoFondo' Color='@RojoFondo@'/>
  <SolidColorBrush x:Key='AzulTexto' Color='@AzulTexto@'/>
  <SolidColorBrush x:Key='AzulFondo' Color='@AzulFondo@'/>
  <SolidColorBrush x:Key='GrisTexto' Color='@GrisTexto@'/>
  <SolidColorBrush x:Key='GrisFondo' Color='@GrisFondo@'/>

  <!-- Texto -->
  <Style x:Key='Titulo' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='28'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
  </Style>
  <Style x:Key='Subtitulo' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='17'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
  </Style>
  <Style x:Key='Cuerpo' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
  </Style>
  <Style x:Key='Fuerte' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
  </Style>
  <Style x:Key='Secundario' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Foreground' Value='{DynamicResource TextoSecundario}'/>
  </Style>
  <Style x:Key='Cifra' TargetType='TextBlock'>
    <Setter Property='FontSize' Value='30'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
  </Style>

  <!-- Tarjeta -->
  <Style x:Key='EstiloTarjeta' TargetType='Border'>
    <Setter Property='Background' Value='{DynamicResource Tarjeta}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Borde}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='CornerRadius' Value='8'/>
    <Setter Property='Padding' Value='18,16'/>
  </Style>

  <!-- Botones -->
  <Style TargetType='Button'>
    <Setter Property='Background' Value='{DynamicResource Tarjeta}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Borde}'/>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='14,7'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' CornerRadius='5' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource Seleccion}'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='BotonAcento' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'>
    <Setter Property='Background' Value='{DynamicResource Acento}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Acento}'/>
    <Setter Property='Foreground' Value='{DynamicResource AcentoTexto}'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' CornerRadius='5' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                  BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.9'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='b' Property='Opacity' Value='0.75'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='BotonEnlace' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Acento}'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <ContentPresenter x:Name='c'/>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='c' Property='Opacity' Value='0.75'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Menú lateral -->
  <Style x:Key='Nav' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Margin' Value='6,2'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='f' CornerRadius='5' Background='Transparent' Padding='14,9'>
            <Grid>
              <Rectangle x:Name='barra' Width='3' Height='16' RadiusX='1.5' RadiusY='1.5' Fill='{DynamicResource Acento}'
                         HorizontalAlignment='Left' Margin='-14,0,0,0' Visibility='Collapsed'/>
              <ContentPresenter/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='f' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='f' Property='Background' Value='{DynamicResource Seleccion}'/>
              <Setter TargetName='barra' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Selector de opciones (píldoras) -->
  <Style x:Key='Segmento' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Margin' Value='0,0,6,0'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RadioButton'>
          <Border x:Name='f' CornerRadius='14' Background='{DynamicResource Tarjeta}' BorderBrush='{DynamicResource Borde}'
                  BorderThickness='1' Padding='12,5'>
            <ContentPresenter VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='f' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='f' Property='Background' Value='{DynamicResource Acento}'/>
              <Setter TargetName='f' Property='BorderBrush' Value='{DynamicResource Acento}'/>
              <Setter Property='Foreground' Value='{DynamicResource AcentoTexto}'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Casilla -->
  <Style TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Border x:Name='caja' Width='20' Height='20' CornerRadius='4' BorderThickness='1' BorderBrush='{DynamicResource BordeFuerte}'
                    Background='{DynamicResource Tarjeta}' VerticalAlignment='Center'>
              <TextBlock x:Name='marca' Text='&#xE73E;' FontFamily='Segoe MDL2 Assets' FontSize='12' Foreground='{DynamicResource AcentoTexto}'
                         HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/>
            </Border>
            <ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='caja' Property='BorderBrush' Value='{DynamicResource Acento}'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='caja' Property='Background' Value='{DynamicResource Acento}'/>
              <Setter TargetName='caja' Property='BorderBrush' Value='{DynamicResource Acento}'/>
              <Setter TargetName='marca' Property='Visibility' Value='Visible'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.35'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Interruptor -->
  <Style x:Key='Interruptor' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <StackPanel Orientation='Horizontal' Background='Transparent'>
            <Grid Width='40' Height='20' VerticalAlignment='Center'>
              <Border x:Name='pista' CornerRadius='10' BorderThickness='1' BorderBrush='{DynamicResource BordeFuerte}' Background='Transparent'/>
              <Ellipse x:Name='bola' Width='12' Height='12' Fill='{DynamicResource TextoSecundario}' HorizontalAlignment='Left' Margin='4,0,0,0'/>
            </Grid>
            <ContentPresenter Margin='10,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='pista' Property='Background' Value='{DynamicResource Acento}'/>
              <Setter TargetName='pista' Property='BorderBrush' Value='{DynamicResource Acento}'/>
              <Setter TargetName='bola' Property='Fill' Value='{DynamicResource AcentoTexto}'/>
              <Setter TargetName='bola' Property='HorizontalAlignment' Value='Right'/>
              <Setter TargetName='bola' Property='Margin' Value='0,0,4,0'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Barra de progreso -->
  <Style TargetType='ProgressBar'>
    <Setter Property='Height' Value='6'/>
    <Setter Property='Foreground' Value='{DynamicResource Acento}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ProgressBar'>
          <Grid>
            <Border x:Name='PART_Track' CornerRadius='3' Background='{DynamicResource Borde}'/>
            <Border x:Name='PART_Indicator' CornerRadius='3' Background='{TemplateBinding Foreground}' HorizontalAlignment='Left'/>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Barras de desplazamiento finas -->
  <Style x:Key='BarraVacia' TargetType='RepeatButton'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='IsTabStop' Value='False'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='BarraPulgar' TargetType='Thumb'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Thumb'><Border CornerRadius='3' Background='{DynamicResource Pulgar}' Margin='2'/></ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Style='{StaticResource BarraVacia}'/></Track.DecreaseRepeatButton>
            <Track.Thumb><Thumb Style='{StaticResource BarraPulgar}'/></Track.Thumb>
            <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Style='{StaticResource BarraVacia}'/></Track.IncreaseRepeatButton>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property='Orientation' Value='Horizontal'>
        <Setter Property='Width' Value='Auto'/>
        <Setter Property='MinWidth' Value='0'/>
        <Setter Property='Height' Value='10'/>
        <Setter Property='MinHeight' Value='10'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='ScrollBar'>
              <Track x:Name='PART_Track'>
                <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageLeftCommand' Style='{StaticResource BarraVacia}'/></Track.DecreaseRepeatButton>
                <Track.Thumb><Thumb Style='{StaticResource BarraPulgar}'/></Track.Thumb>
                <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageRightCommand' Style='{StaticResource BarraVacia}'/></Track.IncreaseRepeatButton>
              </Track>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='{DynamicResource Tarjeta}'/>
    <Setter Property='Foreground' Value='{DynamicResource Texto}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Borde}'/>
    <Setter Property='Padding' Value='8,5'/>
  </Style>
</ResourceDictionary>";
}
