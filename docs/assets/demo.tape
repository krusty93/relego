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
Set Theme "Catppuccin Mocha"
Set TypingSpeed 60ms
Set WindowBar Colorful
Set WindowBarSize 40
Set Padding 20
Set Margin 20
Set MarginFill "#b56b39"
Set BorderRadius 10

# --- Launch the TUI ---
Sleep 1s
Type "docker compose run --rm -v $PWD:/kindle:ro relego-cli"
Enter
Sleep 5s

# --- Import highlights ---
Type "i"
Sleep 1s

# Type the path to the clippings file
Type "/kindle/kindle-highlights.txt"
Sleep 500ms
Enter
Sleep 4s

# --- Go to Settings ---
Type "s"
Sleep 2s

# Select Kindle Email field (first field) and edit it
Enter
Sleep 500ms
# Clear existing value and type new email
Ctrl+A
Type "mykindleemail@kindle.com"
Enter
Sleep 2s

# Go back to book list
Escape
Sleep 2s

# --- View a highlight ---
# Navigate to the first book and open it
Enter
Sleep 2s

# Open the first highlight detail
Enter
Sleep 2s

# Switch to action menu (Tab to actions pane)
Tab
Sleep 1s

# Select "Set weight" (first action)
Enter
Sleep 1s

# Set weight to 5
Type "5"
Sleep 1s
Enter
Sleep 2s

# Close detail viewer
Escape
Sleep 1s

# Go back to book list
Escape
Sleep 2s

# --- Quit ---
Type "q"
Sleep 1s
Type "y"
Sleep 2s
