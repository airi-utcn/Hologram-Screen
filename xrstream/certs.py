"""A self-signed certificate, so a headset can reach the viewer.

WebXR only starts in a secure context.  `http://localhost` counts as one, but a
headset on the network reaches this machine by its LAN address, which does not
-- so the server has to speak https, and for a demo on a local network a
certificate it signs itself is enough.  Nobody else vouches for it, so the
browser shows a warning that has to be accepted once, by hand, in the headset.
"""
from __future__ import annotations

import datetime as _dt
import ipaddress
import socket
from pathlib import Path

DEFAULT_DIR = Path(".certs")


def lan_addresses() -> list[str]:
    """Every IPv4 address this machine answers on, best effort."""
    found: list[str] = []
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ip = info[4][0]
            if ip not in found and not ip.startswith("127."):
                found.append(ip)
    except socket.gaierror:
        pass
    # the address a route to the outside would leave from: catches the case
    # where the hostname resolves to something useless
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("10.255.255.255", 1))
        ip = s.getsockname()[0]
        if ip not in found and not ip.startswith("127."):
            found.append(ip)
    except OSError:
        pass
    finally:
        s.close()
    return found


def ensure_cert(directory: Path | str = DEFAULT_DIR,
                hosts: list[str] | None = None) -> tuple[Path, Path]:
    """Return (certificate, key), making them the first time they are asked for.

    The certificate names localhost and this machine's addresses, so the same
    file works whether the viewer is opened on the desktop or from a headset.
    A stale certificate -- the laptop moved to another network and has a new
    address -- is replaced rather than handed back.
    """
    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import ec
    from cryptography.x509.oid import NameOID

    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    cert_path, key_path = directory / "viewer.crt", directory / "viewer.key"

    names = ["localhost"] + (hosts if hosts is not None else lan_addresses())
    if cert_path.exists() and key_path.exists() and _covers(cert_path, names):
        return cert_path, key_path

    key = ec.generate_private_key(ec.SECP256R1())
    subject = x509.Name([
        x509.NameAttribute(NameOID.COMMON_NAME, "uc7 lesson viewer"),
        x509.NameAttribute(NameOID.ORGANIZATION_NAME, "uc7 xr streaming"),
    ])
    now = _dt.datetime.now(_dt.timezone.utc)
    cert = (x509.CertificateBuilder()
            .subject_name(subject)
            .issuer_name(subject)
            .public_key(key.public_key())
            .serial_number(x509.random_serial_number())
            .not_valid_before(now - _dt.timedelta(days=1))
            .not_valid_after(now + _dt.timedelta(days=825))
            .add_extension(x509.SubjectAlternativeName(_san(names)), critical=False)
            .add_extension(x509.BasicConstraints(ca=False, path_length=None), critical=True)
            .sign(key, hashes.SHA256()))

    cert_path.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    key_path.write_bytes(key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption()))
    return cert_path, key_path


def _san(names: list[str]) -> list:
    from cryptography import x509
    out = []
    for n in names:
        try:
            out.append(x509.IPAddress(ipaddress.ip_address(n)))
        except ValueError:
            out.append(x509.DNSName(n))
    out.append(x509.IPAddress(ipaddress.ip_address("127.0.0.1")))
    return out


def _covers(cert_path: Path, names: list[str]) -> bool:
    """True when an existing certificate still names every address we need."""
    from cryptography import x509
    try:
        cert = x509.load_pem_x509_certificate(cert_path.read_bytes())
        san = cert.extensions.get_extension_for_class(x509.SubjectAlternativeName).value
    except Exception:
        return False
    if cert.not_valid_after_utc <= _dt.datetime.now(_dt.timezone.utc):
        return False
    have = {str(v) for v in san.get_values_for_type(x509.DNSName)}
    have |= {str(v) for v in san.get_values_for_type(x509.IPAddress)}
    return all(n in have for n in names)
