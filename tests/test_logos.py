"""The logo folder behind the page strip and the pair hung in the room."""
from xrstream import server


def test_reports_the_folder_in_name_order(tmp_path, monkeypatch):
    monkeypatch.setattr(server, "CLIENT_DIR", tmp_path)
    (tmp_path / "logos").mkdir()
    (tmp_path / "logos" / "2-b.png").write_bytes(b"x")
    (tmp_path / "logos" / "1-a.svg").write_bytes(b"x")
    (tmp_path / "logos" / "README.md").write_text("not a logo")
    names = [u.split("?")[0] for u in server.logos()]
    assert names == ["/client/logos/1-a.svg", "/client/logos/2-b.png"]


def test_each_url_carries_the_files_timestamp(tmp_path, monkeypatch):
    """A swapped logo must not be served from the browser's cache."""
    monkeypatch.setattr(server, "CLIENT_DIR", tmp_path)
    (tmp_path / "logos").mkdir()
    f = tmp_path / "logos" / "1-a.svg"
    f.write_bytes(b"x")
    first = server.logos()[0]
    assert "?v=" in first
    import os
    os.utime(f, (0, 1_700_000_000))
    assert server.logos()[0] != first


def test_no_folder_is_not_an_error(tmp_path, monkeypatch):
    monkeypatch.setattr(server, "CLIENT_DIR", tmp_path)
    assert server.logos() == []
