# THE DIALOGS OF A LAUNCHER, IN FRONT. When Thalia starts an App, its launcher is not the window in front - Thalia is, filling the screen - and a dialog it opens would appear
# behind Thalia, unseen. $Front is an invisible window that stays above the others: the dialogs are opened over it, so that they come up in front of Thalia.
# Dot-sourced by the launchers after Windows.Forms is loaded:  . "$PSScriptRoot\_front.ps1"
#   $d.ShowDialog($Front)    [Windows.Forms.MessageBox]::Show($Front, ...)    InputBoxFront $prompt $title $default
$Front = New-Object Windows.Forms.Form -Property @{ TopMost = $true; ShowInTaskbar = $false; FormBorderStyle = 'None'; StartPosition = 'CenterScreen'; Width = 1; Height = 1; Opacity = 0 }
$Front.Show(); $Front.Activate()

# A line of text asked in a small window that stays in front (Microsoft.VisualBasic's InputBox cannot be given a window to stay over). '' when the User cancels, as InputBox.
function InputBoxFront($prompt, [string]$title = '', [string]$default = '') {
    if ($prompt -is [array]) { $title = "$($prompt[1])"; $default = "$($prompt[2])"; $prompt = "$($prompt[0])" }    # called as InputBox was: InputBoxFront(prompt, title, default)
    $f = New-Object Windows.Forms.Form -Property @{ Text = $title; TopMost = $true; FormBorderStyle = 'FixedDialog'; MaximizeBox = $false; MinimizeBox = $false; StartPosition = 'CenterScreen'; Width = 520; Height = 220; ShowInTaskbar = $true }
    $l = New-Object Windows.Forms.Label -Property @{ Text = $prompt; Left = 12; Top = 10; Width = 480; Height = 90 }
    $t = New-Object Windows.Forms.TextBox -Property @{ Text = $default; Left = 12; Top = 105; Width = 480 }
    $ok = New-Object Windows.Forms.Button -Property @{ Text = 'OK'; Left = 316; Top = 140; Width = 85; DialogResult = 'OK' }
    $no = New-Object Windows.Forms.Button -Property @{ Text = 'Cancel'; Left = 407; Top = 140; Width = 85; DialogResult = 'Cancel' }
    $f.Controls.AddRange(@($l, $t, $ok, $no)); $f.AcceptButton = $ok; $f.CancelButton = $no
    $f.Add_Shown({ $f.Activate(); $t.Focus() })
    $answer = if ($f.ShowDialog($Front) -eq 'OK') { $t.Text } else { '' }
    $f.Dispose(); $answer
}
