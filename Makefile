SHELL := /bin/bash

.PHONY: help submodules submodule-status check-submodules

help:
	@echo "Rok Mahjong repository targets"
	@echo ""
	@echo "  make submodules         Initialize MonoGame, Forma and XamlX"
	@echo "  make submodule-status   Show recursive submodule state"
	@echo "  make check-submodules   Verify pinned commits are remotely reachable"
	@echo ""
	@echo "Build/test targets are added with the M0 solution skeleton."

submodules:
	git submodule update --init third_party/MonoGame third_party/Forma
	git -C third_party/Forma submodule update --init external/XamlX

submodule-status:
	git submodule status --recursive

check-submodules:
	./scripts/check-submodules.sh
