"""The self-signed certificate that lets a headset reach the viewer."""
from xrstream import certs


def test_makes_a_certificate_and_key(tmp_path):
    cert, key = certs.ensure_cert(tmp_path, hosts=["192.168.1.7"])
    assert cert.exists() and key.exists()
    assert cert.read_bytes().startswith(b"-----BEGIN CERTIFICATE-----")
    assert b"PRIVATE KEY" in key.read_bytes()


def test_names_localhost_and_the_asked_for_address(tmp_path):
    from cryptography import x509
    certs.ensure_cert(tmp_path, hosts=["192.168.1.7"])
    cert = x509.load_pem_x509_certificate((tmp_path / "viewer.crt").read_bytes())
    san = cert.extensions.get_extension_for_class(x509.SubjectAlternativeName).value
    names = {str(v) for v in san.get_values_for_type(x509.DNSName)}
    names |= {str(v) for v in san.get_values_for_type(x509.IPAddress)}
    assert {"localhost", "192.168.1.7", "127.0.0.1"} <= names


def test_reuses_one_that_still_fits(tmp_path):
    certs.ensure_cert(tmp_path, hosts=["192.168.1.7"])
    first = (tmp_path / "viewer.crt").read_bytes()
    certs.ensure_cert(tmp_path, hosts=["192.168.1.7"])
    assert (tmp_path / "viewer.crt").read_bytes() == first


def test_replaces_one_that_misses_the_new_address(tmp_path):
    """Moving the laptop to another network must not leave a useless file."""
    certs.ensure_cert(tmp_path, hosts=["192.168.1.7"])
    first = (tmp_path / "viewer.crt").read_bytes()
    certs.ensure_cert(tmp_path, hosts=["10.0.0.4"])
    assert (tmp_path / "viewer.crt").read_bytes() != first
