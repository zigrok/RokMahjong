SHELL := /bin/bash

LEVEL ?= 1
DEVICE ?= mouse

.PHONY: help submodules submodule-status check-submodules progress progress-desktop build run test test-core test-game test-all playtest gallery

help:
	@echo "Rok Mahjong repository targets"
	@echo ""
	@echo "  make submodules         Initialize MonoGame, Forma and XamlX"
	@echo "  make submodule-status   Show recursive submodule state"
	@echo "  make check-submodules   Verify pinned commits are remotely reachable"
	@echo "  make progress           Show overall plan checklist progress"
	@echo "  make progress-desktop   Show M0-M5 desktop checklist progress"
	@echo "  make build              Build the solution"
	@echo "  make run                Run the desktop game"
	@echo "  make test               Run core and game tests"
	@echo "  make test-all           Run the whole solution test suite"
	@echo "  make playtest LEVEL=1 DEVICE=mouse"
	@echo "  make gallery            Capture and validate desktop screenshots"

submodules:
	git submodule update --init third_party/MonoGame third_party/Forma
	git -C third_party/Forma submodule update --init external/XamlX

submodule-status:
	git submodule status --recursive

check-submodules:
	./scripts/check-submodules.sh

progress:
	./tools/plan-progress.sh docs/plan.md

progress-desktop:
	./tools/plan-progress.sh docs/plan.md --phases M0,M1,M2,M3,M4,M5

build:
	dotnet build RokMahjong.slnx

run:
	dotnet run --project src/RokMahjong.Desktop

test: test-core test-game

test-core:
	dotnet test tests/RokMahjong.Core.Tests/RokMahjong.Core.Tests.csproj

test-game:
	dotnet test tests/RokMahjong.Game.Tests/RokMahjong.Game.Tests.csproj

test-all:
	dotnet test RokMahjong.slnx

playtest:
	dotnet run --project tools/RokMahjong.Playtest -- --level $(LEVEL) --device $(DEVICE)

gallery:
	./tools/gallery.sh
