from vos_service import Config, parse_args, usage

THIS_HANDLER = "python-echo-handler"


def test_parse_args_reads_the_standard_flags_and_defaults_nothing():
    cfg = parse_args(["--port=5103", "--myceliumUrl=https://localhost:7243/"], {})
    assert cfg.port == 5103
    assert cfg.mycelium_url == "https://localhost:7243"
    assert cfg.issuer == ""
    assert cfg.audience == ""


def test_parse_args_refuses_a_verification_key_with_no_recipient_name():
    """Each handler is addressed by its own name, so no shared default could be right."""
    environment = {"VerificationKey": "ZW52aXJvbm1lbnQta2V5"}
    assert parse_args(
        ["--port=5103", "--myceliumUrl=https://x", "--issuer=VillageOS"], environment) is None
    assert parse_args(
        ["--port=5103", "--myceliumUrl=https://x", f"--audience={THIS_HANDLER}"], environment) is None


def test_parse_args_takes_credentials_from_the_environment():
    cfg = parse_args(
        ["--port=5103", "--myceliumUrl=https://localhost:7243",
         "--issuer=VillageOS", f"--audience={THIS_HANDLER}"],
        {"Token": "environment-token", "VerificationKey": "ZW52aXJvbm1lbnQta2V5"},
    )
    assert cfg.token == "environment-token"
    assert cfg.verification_key == "ZW52aXJvbm1lbnQta2V5"


def test_parse_args_ignores_credentials_given_as_flags():
    cfg = parse_args(
        ["--port=5103", "--myceliumUrl=https://localhost:7243",
         "--token=flag-token", "--verificationKey=flag-key"],
        {},
    )
    assert cfg.token is None
    assert cfg.verification_key is None


def test_parse_args_refuses_a_port_outside_the_range_and_a_missing_flag():
    assert parse_args(["--port=0", "--myceliumUrl=https://x"], {}) is None
    assert parse_args(["--port=70000", "--myceliumUrl=https://x"], {}) is None
    assert parse_args(["--port=abc", "--myceliumUrl=https://x"], {}) is None
    assert parse_args(["--port=5103"], {}) is None
    assert parse_args(["--myceliumUrl=https://x"], {}) is None


def test_a_service_that_was_not_given_its_flags_is_the_empty_config():
    assert Config().port == 0


def test_usage_tells_the_reader_to_launch_through_the_virtual_environment():
    assert ".venv/bin/python app.py" in usage("app.py")


def test_usage_names_the_program_it_was_asked_about():
    assert ".venv/bin/python service.py" in usage("service.py")
