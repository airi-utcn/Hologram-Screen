"""The two logo folders: one for the page, one for the wall of the room."""
from xrstream import server


def test_reports_the_folder_in_name_order(tmp_path, monkeypatch):
    monkeypatch.setattr(server, "CLIENT_DIR", tmp_path)
    (tmp_path / "logos").mkdir()
    (tmp_path / "logos" / "2-b.png").write_bytes(b"x")
    (tmp_path / "logos" / "1-a.svg").write_bytes(b"x")
    (tmp_path / "logos" / "README.md").write_text("not a logo")
    assert server.logos()["page"] == ["/client/logos/1-a.svg", "/client/logos/2-b.png"]


def test_the_wall_falls_back_to_the_page(tmp_path, monkeypatch):
    monkeypatch.setattr(server, "CLIENT_DIR", tmp_path)
    (tmp_path / "logos").mkdir()
    (tmp_path / "logos" / "1-a.svg").write_bytes(b"x")
    out = server.logos()
    assert out["wall"] == out["page"]


def test_a_wall_folder_wins_for_the_room(tmp_path, monkeypatch):
    """A logo drawn for a white page can be invisible on a dark wall."""
    monkeypatch.setattr(server, "CLIENT_DIR", tmp_path)
    (tmp_path / "logos" / "wall").mkdir(parents=True)
    (tmp_path / "logos" / "1-a.svg").write_bytes(b"x")
    (tmp_path / "logos" / "wall" / "1-a.svg").write_bytes(b"x")
    out = server.logos()
    assert out["page"] == ["/client/logos/1-a.svg"]
    assert out["wall"] == ["/client/logos/wall/1-a.svg"]


def test_no_folder_is_not_an_error(tmp_path, monkeypatch):
    monkeypatch.setattr(server, "CLIENT_DIR", tmp_path)
    assert server.logos() == {"page": [], "wall": []}
