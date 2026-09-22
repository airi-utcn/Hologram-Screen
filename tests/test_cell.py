"""A class sharing one cell."""
import pytest

from xrstream import cell


def test_seats_are_places_in_the_room():
    from xrstream import scenarios
    scene = scenarios.build("classroom")
    seats = cell.seats_for(scene, 12)
    assert len(seats) == 12
    assert len({tuple(s.eye) for s in seats}) == 12      # nobody sits on a lap


def test_unicast_shares_nothing_and_a_hub_does():
    lone = cell.run(mode="unicast", students=6, mbps=25, lesson="anatomy", seconds=4)
    hub = cell.run(mode="hub", students=6, mbps=25, lesson="anatomy", seconds=4)
    assert lone.sharing_gain == pytest.approx(1.0, abs=0.01)
    assert hub.sharing_gain > 1.5
    # and the sharing is what makes it cheaper, for the same class and link
    assert hub.cell_mbytes < lone.cell_mbytes


def test_a_crowded_cell_is_where_sharing_pays():
    """With a class this size, unicast spends its link on copies."""
    lone = cell.run(mode="unicast", students=16, mbps=25, lesson="anatomy", seconds=8)
    hub = cell.run(mode="hub", students=16, mbps=25, lesson="anatomy", seconds=8)
    assert hub.worst_qoe > lone.worst_qoe
    assert hub.mean_qoe > lone.mean_qoe


def test_television_serves_the_room_but_not_the_person():
    """Broadcast alone cannot answer what one student needs and others do not."""
    tv = cell.run(mode="shared", students=2, mbps=25, lesson="anatomy", seconds=6)
    hub = cell.run(mode="hub", students=2, mbps=25, lesson="anatomy", seconds=6)
    assert hub.worst_qoe > tv.worst_qoe


def test_the_same_class_twice_gives_the_same_numbers():
    a = cell.run(mode="hub", students=4, mbps=25, lesson="anatomy", seconds=4)
    b = cell.run(mode="hub", students=4, mbps=25, lesson="anatomy", seconds=4)
    assert a.row() == b.row()


def test_an_unknown_scheduler_is_refused():
    with pytest.raises(KeyError):
        cell.Cell(mode="wishful")


def test_two_people_never_share_a_capture_by_accident():
    """The capture cache used to key on id(), which Python reuses.

    A body that has been collected leaves its address free for the next one,
    and the cache then hands the new person the old person's points -- which
    blows up as an index error when the two were sampled at different
    densities. Every body now carries a number of its own.
    """
    import gc

    from xrstream import body as bodies

    from xrstream import scenarios

    palette = scenarios._palette((0.2, 0.3, 0.4))
    first = bodies.Body(3000, palette, seed=1)
    seen = first.uid
    del first
    gc.collect()
    second = bodies.Body(9000, palette, seed=2)
    assert second.uid != seen


def test_many_rooms_in_one_process_do_not_collide():
    """Building scene after scene is what surfaced the cache bug."""
    from xrstream import scenarios
    for name in ("classroom", "chemistry_lab", "gym", "classroom"):
        scene = scenarios.build(name)
        for o in scene:
            if o.dynamic:
                o.level(1, 0)          # a capture at a coarser level


def test_a_united_class_is_served_like_television():
    """With everyone watching the same thing, nearly every byte is shared."""
    r = cell.run(mode="hub", students=12, mbps=15, lesson="titration",
                 seconds=5, attention=1.0)
    assert r.sharing_gain > 4.0


def test_a_scattered_class_shares_less_and_the_hub_notices():
    """When they look elsewhere, less is common -- and more must be personal."""
    together = cell.run(mode="hub", students=12, mbps=15, lesson="titration",
                        seconds=5, attention=1.0)
    apart = cell.run(mode="hub", students=12, mbps=15, lesson="titration",
                     seconds=5, attention=0.25)
    assert apart.sharing_gain < together.sharing_gain


def test_pricing_beats_broadcasting_when_the_class_scatters():
    """The point of pricing per byte: television cannot answer one student."""
    tv = cell.run(mode="shared", students=12, mbps=15, lesson="titration",
                  seconds=6, attention=0.5)
    ours = cell.run(mode="hub", students=12, mbps=15, lesson="titration",
                    seconds=6, attention=0.5)
    assert ours.worst_qoe > tv.worst_qoe
    assert ours.mean_qoe > tv.mean_qoe


def test_attention_changes_who_looks_where():
    from xrstream import scenarios
    scene = scenarios.build("classroom")
    kinds = {s.kind for s in cell.seats_for(scene, 12, attention=0.5)}
    assert kinds == set(cell.KINDS)
    assert {s.kind for s in cell.seats_for(scene, 12, attention=1.0)} == {"attentive"}
