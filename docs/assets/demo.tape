# Relego TUI Demo — recorded with VHS (https://github.com/charmbracelet/vhs)
#
# Prerequisites:
#   1. Have Docker running with relego-server started:
#        docker compose --profile server up -d
#   2. Run from repo root (Windows):
#        docker run --rm -v "${PWD}:/vhs" -v /var/run/docker.sock:/var/run/docker.sock --network host ghcr.io/charmbracelet/vhs docs/assets/demo.tape
#      or Linux/macOS:
#        docker run --rm -v "$(pwd):/vhs" -v /var/run/docker.sock:/var/run/docker.sock --network host ghcr.io/charmbracelet/vhs docs/assets/demo.tape

Output docs/assets/tui-demo.gif
Set FontSize 16
Set Width 1400
Set Height 700
Set Theme "Catppuccin Mocha" # "Catppuccin Macchiato" # "Adventure" # "Jackie Brown"
Set TypingSpeed 60ms
Set WindowBar Colorful
Set Padding 20
Set Margin 20
Set MarginFill "#d4a05e"
Set BorderRadius 10

# --- Launch the TUI ---
Sleep 1s
Type "docker compose run --rm -v $PWD:/kindle:ro relego-cli"
Enter
Sleep 5s

# --- Import highlights ---
Type "i"
Sleep 2s
Backspace 45
Sleep 500ms
Type "/kindle/docs/examples/kindle-highlights.txt"
Sleep 1s
Enter
Sleep 1s
Type "r"
Sleep 5s

# --- View a highlight ---
# Navigate to the first book and open it
Enter
Sleep 2s

# Open the first highlight detail
Enter
Sleep 2s

# Select "Set weight" (first action)
Enter
Sleep 1s

# Set weight to 5
Type "5"
Sleep 1s
Enter
Sleep 2s

# Open second item
Down 1
Sleep 1s
Enter
Sleep 1s

# Exclude highlight
Down
Enter

# Go back
Escape
Sleep 1s

# Exclude second item
Down
Sleep 1s
Enter
Sleep 1s

# Delete last item
Down 4
Sleep 1s
Enter
Sleep 1s
Type "Y"

# Pause
Sleep 3s
