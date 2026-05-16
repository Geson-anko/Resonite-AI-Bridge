import resoio


def test_version_is_a_string():
    assert isinstance(resoio.__version__, str)
    assert resoio.__version__
