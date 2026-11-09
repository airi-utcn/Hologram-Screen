"""
How to plug the exporter into the Python script that builds the OCT figure.

Add these lines at the end of that script, next to the existing
fig.write_html(...) / fig.show(). Nothing else in the script changes.
"""
import sys
sys.path.insert(0, "/path/to/hologram-oct/exporter")   # or copy plotly_to_hld.py next to the script
from plotly_to_hld import export_figure

# `fig` is the plotly.graph_objects.Figure the script already builds
# (volume trace + mesh3d traces). Name it per patient/eye:
export_figure(fig, "/path/to/hologram-oct/out/W_I_OS")

# Options:
#   depth_scale=1/3      undo the x3 depth exaggeration (true proportions)
#   flip_depth=True      turn the whole scan upside down
#   volume_depth="auto"  detect the volume/mesh depth mirror (default);
#                        set "same" once the script itself is fixed
#   crop=False           keep empty depth layers of the volume
