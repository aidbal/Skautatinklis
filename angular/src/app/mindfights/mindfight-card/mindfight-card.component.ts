import { Component, OnInit, Injector, Input } from '@angular/core';
import { AppComponentBase } from '@shared/app-component-base';
import { MindfightDto, PlayerDto } from 'shared/service-proxies/service-proxies';

@Component({
    selector: 'app-mindfight-card',
    templateUrl: './mindfight-card.component.html',
    styleUrls: ['./mindfight-card.component.css']
})
export class MindfightCardComponent extends AppComponentBase implements OnInit {
    @Input() mindfight: MindfightDto;
    @Input() playerInfo: PlayerDto;
    canEdit = false;
    canEvaluate = false;

    constructor(injector: Injector) {
        super(injector);
    }

    ngOnInit() {
        this.canEditMindfight();
        this.canEvaluateMindfight();
    }

    onRegisterChange(registrationChangeObject): void {
        if (registrationChangeObject.createEvent) {
            this.mindfight.registeredTeamsCount += 1;
        } else {
            this.mindfight.registeredTeamsCount -= 1;
        }
    }

    isMindfightCreator(): boolean {
        return abp.session.userId === this.mindfight.creatorId;
    }

    canEditMindfight() {
        if (this.isMindfightCreator() || abp.auth.isGranted("ManageMindfights")) {
            this.canEdit = true;
        }
    }

    canEvaluateMindfight() {
        if (this.canEditMindfight()) {
            this.canEvaluate = true;
        } else {
            this.mindfight.usersAllowedToEvaluate.forEach((userEmail) => {
                if (userEmail.toUpperCase() === this.playerInfo.emailAddress.toUpperCase()) {
                    this.canEvaluate = true;
                    return false;
                }
            });
        }
    }
}
